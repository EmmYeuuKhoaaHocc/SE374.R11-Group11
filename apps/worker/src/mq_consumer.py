import asyncio
import base64
import json
import logging
import time
from typing import Any, Dict, Optional

import aio_pika
from aio_pika import DeliveryMode, ExchangeType, Message

from .config import settings
from .inference import inference_engine

logger = logging.getLogger("neuroscan.worker.mq")


class RabbitMqInferenceConsumer:
    """Async RabbitMQ topic consumer/publisher for UQ brain tumor segmentation."""

    def __init__(self) -> None:
        self._connection: Optional[aio_pika.abc.AbstractRobustConnection] = None
        self._channel: Optional[aio_pika.abc.AbstractChannel] = None
        self._exchange: Optional[aio_pika.abc.AbstractExchange] = None
        self._consumer_tag: Optional[str] = None
        self._queue: Optional[aio_pika.abc.AbstractQueue] = None

    @property
    def is_enabled(self) -> bool:
        return bool(settings.RABBITMQ_HOST.strip())

    async def start(self) -> None:
        if not self.is_enabled:
            logger.info("RABBITMQ_HOST is not configured; RabbitMQ consumer disabled (HTTP mode only).")
            return

        if not settings.RABBITMQ_USER or not settings.RABBITMQ_PASS:
            raise RuntimeError(
                "RABBITMQ_HOST is set, but RABBITMQ_USER / RABBITMQ_PASS were not provided. "
                "Pass credentials via CLI flags (--rmq-user / --rmq-pass) or environment variables."
            )

        logger.info(
            "Connecting worker to RabbitMQ %s:%s (vhost=%s, exchange=%s)",
            settings.RABBITMQ_HOST,
            settings.RABBITMQ_PORT,
            settings.RABBITMQ_VHOST,
            settings.RABBITMQ_EXCHANGE,
        )

        self._connection = await aio_pika.connect_robust(
            host=settings.RABBITMQ_HOST,
            port=settings.RABBITMQ_PORT,
            login=settings.RABBITMQ_USER,
            password=settings.RABBITMQ_PASS,
            virtualhost=settings.RABBITMQ_VHOST,
            heartbeat=60,
        )
        self._channel = await self._connection.channel()
        await self._channel.set_qos(prefetch_count=1)

        self._exchange = await self._channel.declare_exchange(
            settings.RABBITMQ_EXCHANGE,
            ExchangeType.TOPIC,
            durable=True,
        )

        self._queue = await self._channel.declare_queue(
            settings.RABBITMQ_REQUEST_QUEUE,
            durable=True,
        )
        await self._queue.bind(self._exchange, routing_key=settings.RABBITMQ_REQUEST_ROUTING_KEY)
        self._consumer_tag = await self._queue.consume(self._on_message)
        logger.info(
            "RabbitMQ worker listening on queue '%s' bound to '%s'",
            settings.RABBITMQ_REQUEST_QUEUE,
            settings.RABBITMQ_REQUEST_ROUTING_KEY,
        )

    async def stop(self) -> None:
        try:
            if self._queue is not None and self._consumer_tag is not None:
                await self._queue.cancel(self._consumer_tag)
        except Exception:
            logger.exception("Failed cancelling RabbitMQ consumer tag")
        try:
            if self._connection is not None and not self._connection.is_closed:
                await self._connection.close()
        except Exception:
            logger.exception("Failed closing RabbitMQ connection")
        finally:
            self._consumer_tag = None
            self._queue = None
            self._exchange = None
            self._channel = None
            self._connection = None

    async def _publish_json(self, routing_key: str, payload: Dict[str, Any]) -> None:
        if self._exchange is None:
            return
        body = json.dumps(payload).encode("utf-8")
        msg = Message(
            body=body,
            content_type="application/json",
            delivery_mode=DeliveryMode.PERSISTENT,
        )
        await self._exchange.publish(msg, routing_key=routing_key)

    async def _on_message(self, message: aio_pika.abc.AbstractIncomingMessage) -> None:
        async with message.process(requeue=False):
            payload: Dict[str, Any] = {}
            case_id = ""
            mri_scan_id = ""
            correlation_id = ""
            task_id = ""

            try:
                payload = json.loads(message.body.decode("utf-8"))
                case_id = str(payload["case_id"])
                mri_scan_id = str(payload["mri_scan_id"])
                correlation_id = str(payload.get("correlation_id") or "")
                num_passes = int(payload.get("num_passes") or settings.MC_NUM_PASSES)
                sequences = payload["sequences"]

                flair_bytes = base64.b64decode(sequences["flair_b64"])
                t1_bytes = base64.b64decode(sequences["t1_b64"])
                t1ce_bytes = base64.b64decode(sequences["t1ce_b64"])
                t2_bytes = base64.b64decode(sequences["t2_b64"])
            except Exception as exc:
                logger.exception("Invalid inference.request payload")
                if case_id:
                    await self._publish_json(
                        "inference.result.failed",
                        {
                            "case_id": case_id,
                            "mri_scan_id": mri_scan_id,
                            "correlation_id": correlation_id,
                            "error": f"Invalid inference request payload: {exc}",
                        },
                    )
                return

            loop = asyncio.get_running_loop()

            def emit_progress(stage: str, msg_text: str) -> None:
                fut = asyncio.run_coroutine_threadsafe(
                    self._publish_json(
                        "inference.result.progress",
                        {
                            "case_id": case_id,
                            "mri_scan_id": mri_scan_id,
                            "correlation_id": correlation_id,
                            "stage": stage,
                            "message": msg_text,
                        },
                    ),
                    loop,
                )
                fut.add_done_callback(lambda f: f.exception() if not f.cancelled() else None)

            started_at = time.perf_counter()
            try:
                result = await asyncio.to_thread(
                    inference_engine.run_uq_inference,
                    flair_bytes,
                    t1_bytes,
                    t1ce_bytes,
                    t2_bytes,
                    num_passes,
                    settings.MC_BATCH_SIZE,
                    emit_progress,
                )
                task_id = str(result["task_id"])
                elapsed_ms = int((time.perf_counter() - started_at) * 1000)

                p05_bytes = inference_engine.read_mask_bytes(task_id, 5)
                p50_bytes = inference_engine.read_mask_bytes(task_id, 50)
                p95_bytes = inference_engine.read_mask_bytes(task_id, 95)

                await self._publish_json(
                    "inference.result.completed",
                    {
                        "case_id": case_id,
                        "mri_scan_id": mri_scan_id,
                        "correlation_id": correlation_id,
                        "execution_duration_ms": elapsed_ms,
                        "response": result,
                        "masks_b64": {
                            "p05_nii_gz_b64": base64.b64encode(p05_bytes).decode("ascii"),
                            "p50_nii_gz_b64": base64.b64encode(p50_bytes).decode("ascii"),
                            "p95_nii_gz_b64": base64.b64encode(p95_bytes).decode("ascii"),
                        },
                    },
                )
                logger.info(
                    "Published completed inference for case=%s task=%s (%d ms)",
                    case_id,
                    task_id,
                    elapsed_ms,
                )
            except Exception as exc:
                logger.exception("Inference failed for case=%s", case_id)
                await self._publish_json(
                    "inference.result.failed",
                    {
                        "case_id": case_id,
                        "mri_scan_id": mri_scan_id,
                        "correlation_id": correlation_id,
                        "error": str(exc),
                    },
                )
            finally:
                if task_id:
                    inference_engine.cleanup_task_masks(task_id)


mq_consumer = RabbitMqInferenceConsumer()
