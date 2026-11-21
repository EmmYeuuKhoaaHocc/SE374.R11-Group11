import asyncio
import base64
import json
import logging
import os
import socket
import time
from typing import Any, Dict, Optional

import aio_pika
from aio_pika import DeliveryMode, ExchangeType, Message
from aiormq.exceptions import ChannelPreconditionFailed

from .config import settings
from .inference import inference_engine
from .s3_client import s3_downloader

logger = logging.getLogger("neuroscan.worker.mq")


class RabbitMqInferenceConsumer:
    """Async RabbitMQ topic consumer/publisher with delayed TTL retry and DLQ."""

    def __init__(self) -> None:
        self._connection: Optional[aio_pika.abc.AbstractRobustConnection] = None
        self._channel: Optional[aio_pika.abc.AbstractChannel] = None
        self._pub_channel: Optional[aio_pika.abc.AbstractChannel] = None
        self._pub_exchange: Optional[aio_pika.abc.AbstractExchange] = None
        self._retry_exchange: Optional[aio_pika.abc.AbstractExchange] = None
        self._pub_lock = asyncio.Lock()
        self._consumer_tag: Optional[str] = None
        self._reload_consumer_tag: Optional[str] = None
        self._queue: Optional[aio_pika.abc.AbstractQueue] = None
        self._reload_queue: Optional[aio_pika.abc.AbstractQueue] = None
        self._connect_task: Optional[asyncio.Task] = None
        self._stopping = False
        self.connected = False
        self.last_error: Optional[str] = None

    @property
    def is_enabled(self) -> bool:
        return bool(settings.RABBITMQ_HOST.strip())

    @property
    def retry_exchange_name(self) -> str:
        return f"{settings.RABBITMQ_EXCHANGE}.retry"

    @property
    def dlx_exchange_name(self) -> str:
        return f"{settings.RABBITMQ_EXCHANGE}.dlx"

    @property
    def request_retry_queue_name(self) -> str:
        return f"{settings.RABBITMQ_REQUEST_QUEUE}.retry"

    @property
    def request_dlq_name(self) -> str:
        return f"{settings.RABBITMQ_REQUEST_QUEUE}.dlq"

    async def start(self) -> None:
        if not self.is_enabled:
            logger.info("RABBITMQ_HOST is not configured; RabbitMQ consumer disabled.")
            return

        if not settings.RABBITMQ_USER or not settings.RABBITMQ_PASS:
            raise RuntimeError(
                "RABBITMQ_HOST is set, but RABBITMQ_USER / RABBITMQ_PASS were not provided. "
                "Pass credentials via CLI flags (--rmq-user / --rmq-pass) or environment variables."
            )

        self._stopping = False
        self._connect_task = asyncio.create_task(self._connect_and_consume_loop())

    async def _declare_queue_with_args(
        self,
        queue_name: str,
        arguments: Optional[Dict[str, Any]] = None,
    ) -> aio_pika.abc.AbstractQueue:
        assert self._channel is not None
        try:
            return await self._channel.declare_queue(
                queue_name,
                durable=True,
                arguments=arguments,
            )
        except ChannelPreconditionFailed:
            # Reopen channel after 406 PRECONDITION_FAILED and upgrade legacy empty queue safely.
            logger.warning(
                "Queue '%s' exists with different arguments; attempting safe upgrade when empty...",
                queue_name,
            )
            self._channel = await self._connection.channel()  # type: ignore[union-attr]
            await self._channel.set_qos(prefetch_count=1)
            existing = await self._channel.get_queue(queue_name, ensure=False)
            await existing.delete(if_unused=False, if_empty=True)
            return await self._channel.declare_queue(
                queue_name,
                durable=True,
                arguments=arguments,
            )

    async def _connect_and_consume_loop(self) -> None:
        while not self._stopping:
            try:
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
                    timeout=15,
                )
                self._channel = await self._connection.channel()
                await self._channel.set_qos(prefetch_count=1)

                main_exchange = await self._channel.declare_exchange(
                    settings.RABBITMQ_EXCHANGE,
                    ExchangeType.TOPIC,
                    durable=True,
                )
                retry_exchange = await self._channel.declare_exchange(
                    self.retry_exchange_name,
                    ExchangeType.TOPIC,
                    durable=True,
                )
                dlx_exchange = await self._channel.declare_exchange(
                    self.dlx_exchange_name,
                    ExchangeType.TOPIC,
                    durable=True,
                )

                self._pub_channel = await self._connection.channel()
                self._pub_exchange = await self._pub_channel.declare_exchange(
                    settings.RABBITMQ_EXCHANGE,
                    ExchangeType.TOPIC,
                    durable=True,
                )
                self._retry_exchange = await self._pub_channel.declare_exchange(
                    self.retry_exchange_name,
                    ExchangeType.TOPIC,
                    durable=True,
                )

                dlq = await self._declare_queue_with_args(self.request_dlq_name)
                await dlq.bind(dlx_exchange, routing_key=settings.RABBITMQ_REQUEST_ROUTING_KEY)

                retry_queue = await self._declare_queue_with_args(
                    self.request_retry_queue_name,
                    arguments={
                        "x-message-ttl": settings.RABBITMQ_RETRY_DELAY_MS,
                        "x-dead-letter-exchange": settings.RABBITMQ_EXCHANGE,
                        "x-dead-letter-routing-key": settings.RABBITMQ_REQUEST_ROUTING_KEY,
                    },
                )
                await retry_queue.bind(retry_exchange, routing_key=settings.RABBITMQ_REQUEST_ROUTING_KEY)

                self._queue = await self._declare_queue_with_args(
                    settings.RABBITMQ_REQUEST_QUEUE,
                    arguments={"x-dead-letter-exchange": self.dlx_exchange_name},
                )
                await self._queue.bind(main_exchange, routing_key=settings.RABBITMQ_REQUEST_ROUTING_KEY)
                self._consumer_tag = await self._queue.consume(self._on_message)

                # Per-worker exclusive queue bound to model.reload so all workers reload atomically
                reload_q_name = f"neuroscan.worker.reload.{socket.gethostname()}.{os.getpid()}"
                self._reload_queue = await self._channel.declare_queue(
                    reload_q_name,
                    durable=False,
                    exclusive=True,
                    auto_delete=True,
                )
                await self._reload_queue.bind(main_exchange, routing_key=settings.RABBITMQ_RELOAD_ROUTING_KEY)
                self._reload_consumer_tag = await self._reload_queue.consume(self._on_reload_message)

                self.connected = True
                self.last_error = None
                logger.info(
                    "RabbitMQ worker listening on '%s' + reload queue '%s' (retry='%s' [%dms, max=%d], dlq='%s')",
                    settings.RABBITMQ_REQUEST_QUEUE,
                    reload_q_name,
                    self.request_retry_queue_name,
                    settings.RABBITMQ_RETRY_DELAY_MS,
                    settings.RABBITMQ_MAX_RETRIES,
                    self.request_dlq_name,
                )
                return
            except asyncio.CancelledError:
                raise
            except Exception as exc:
                self.connected = False
                self.last_error = str(exc)
                logger.warning("RabbitMQ worker connection failed (%s); retrying in 5s...", exc)
                await asyncio.sleep(5)

    async def stop(self) -> None:
        self._stopping = True
        if self._connect_task is not None and not self._connect_task.done():
            self._connect_task.cancel()
            try:
                await self._connect_task
            except asyncio.CancelledError:
                pass
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
            self.connected = False
            self._connect_task = None
            self._consumer_tag = None
            self._queue = None
            self._pub_exchange = None
            self._retry_exchange = None
            self._pub_channel = None
            self._channel = None
            self._connection = None

    async def _ensure_pub_exchanges(self) -> None:
        if self._connection is None or self._connection.is_closed:
            raise RuntimeError("RabbitMQ connection is not open")
        if self._pub_channel is None or self._pub_channel.is_closed or self._pub_exchange is None or self._retry_exchange is None:
            self._pub_channel = await self._connection.channel()
            self._pub_exchange = await self._pub_channel.declare_exchange(
                settings.RABBITMQ_EXCHANGE,
                ExchangeType.TOPIC,
                durable=True,
            )
            self._retry_exchange = await self._pub_channel.declare_exchange(
                self.retry_exchange_name,
                ExchangeType.TOPIC,
                durable=True,
            )

    async def _publish_json(self, routing_key: str, payload: Dict[str, Any]) -> None:
        if self._connection is None or self._connection.is_closed:
            logger.warning("Cannot publish '%s': RabbitMQ connection is not open.", routing_key)
            return

        body = json.dumps(payload).encode("utf-8")
        msg = Message(
            body=body,
            content_type="application/json",
            delivery_mode=DeliveryMode.PERSISTENT,
        )

        async with self._pub_lock:
            await self._ensure_pub_exchanges()
            assert self._pub_exchange is not None
            await self._pub_exchange.publish(msg, routing_key=routing_key)

    async def _publish_to_retry(
        self,
        original_message: aio_pika.abc.AbstractIncomingMessage,
        next_retry: int,
        error_text: str,
    ) -> None:
        headers = dict(original_message.headers or {})
        headers["x-retry-count"] = next_retry
        headers["x-last-error"] = error_text[:500]

        retry_msg = Message(
            body=original_message.body,
            content_type=original_message.content_type or "application/json",
            correlation_id=original_message.correlation_id,
            delivery_mode=DeliveryMode.PERSISTENT,
            headers=headers,
        )

        async with self._pub_lock:
            await self._ensure_pub_exchanges()
            assert self._retry_exchange is not None
            await self._retry_exchange.publish(
                retry_msg,
                routing_key=settings.RABBITMQ_REQUEST_ROUTING_KEY,
            )

    @staticmethod
    def _get_retry_count(message: aio_pika.abc.AbstractIncomingMessage) -> int:
        headers = message.headers or {}
        raw = headers.get("x-retry-count", 0)
        try:
            return max(0, int(raw))
        except (TypeError, ValueError):
            return 0

    async def _on_message(self, message: aio_pika.abc.AbstractIncomingMessage) -> None:
        retry_count = self._get_retry_count(message)
        logger.info(
            "Received RabbitMQ message delivery_tag=%s retry=%d/%d size=%d bytes",
            message.delivery_tag,
            retry_count,
            settings.RABBITMQ_MAX_RETRIES,
            len(message.body),
        )

        case_id = ""
        mri_scan_id = ""
        correlation_id = ""
        task_id = ""

        # Step 1: Parse & decode payload (malformed payloads go straight to DLQ without retry)
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
            logger.info(
                "Decoded inference.request case=%s (FLAIR=%d B, T1=%d B, T1ce=%d B, T2=%d B, passes=%d)",
                case_id,
                len(flair_bytes),
                len(t1_bytes),
                len(t1ce_bytes),
                len(t2_bytes),
                num_passes,
            )
        except Exception as exc:
            logger.exception("Invalid inference.request payload; routing directly to DLQ '%s'", self.request_dlq_name)
            if case_id:
                await self._publish_json(
                    "inference.result.failed",
                    {
                        "case_id": case_id,
                        "mri_scan_id": mri_scan_id,
                        "correlation_id": correlation_id,
                        "error": f"Invalid inference request payload (sent to DLQ): {exc}",
                    },
                )
            await message.nack(requeue=False)
            return

        loop = asyncio.get_running_loop()

        def _log_progress_future(fut: asyncio.Future) -> None:
            if not fut.cancelled():
                exc = fut.exception()
                if exc is not None:
                    logger.warning("Failed publishing progress update for case=%s: %s", case_id, exc)

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
            fut.add_done_callback(_log_progress_future)

        # Step 2: Execute UQ GPU inference & publish completion (retry via TTL queue up to MaxRetries, then DLQ)
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

            completed_payload = {
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
            }
            await self._publish_json("inference.result.completed", completed_payload)
            await message.ack()
            logger.info(
                "Published completed inference for case=%s task=%s (%d ms, masks=%d/%d/%d B)",
                case_id,
                task_id,
                elapsed_ms,
                len(p05_bytes),
                len(p50_bytes),
                len(p95_bytes),
            )
        except Exception as exc:
            next_retry = retry_count + 1
            if next_retry <= settings.RABBITMQ_MAX_RETRIES:
                logger.warning(
                    "Inference failed for case=%s (attempt %d/%d): %s. Scheduling retry in %dms via '%s'.",
                    case_id,
                    retry_count,
                    settings.RABBITMQ_MAX_RETRIES,
                    exc,
                    settings.RABBITMQ_RETRY_DELAY_MS,
                    self.request_retry_queue_name,
                )
                try:
                    await self._publish_json(
                        "inference.result.progress",
                        {
                            "case_id": case_id,
                            "mri_scan_id": mri_scan_id,
                            "correlation_id": correlation_id,
                            "stage": "inference_processing",
                            "message": f"Worker gặp lỗi tạm thời, đang thử lại lần {next_retry}/{settings.RABBITMQ_MAX_RETRIES}...",
                        },
                    )
                    await self._publish_to_retry(message, next_retry, str(exc))
                    await message.ack()
                except Exception:
                    logger.exception("Failed scheduling RabbitMQ retry for case=%s; dead-lettering to DLQ.", case_id)
                    await message.nack(requeue=False)
            else:
                logger.exception(
                    "Inference exhausted %d retries for case=%s; dead-lettering to DLQ '%s'.",
                    settings.RABBITMQ_MAX_RETRIES,
                    case_id,
                    self.request_dlq_name,
                )
                await self._publish_json(
                    "inference.result.failed",
                    {
                        "case_id": case_id,
                        "mri_scan_id": mri_scan_id,
                        "correlation_id": correlation_id,
                        "error": f"Worker failed after {settings.RABBITMQ_MAX_RETRIES} retries (moved to DLQ): {exc}",
                    },
                )
                await message.nack(requeue=False)
        finally:
            if task_id:
                inference_engine.cleanup_task_masks(task_id)

    async def _on_reload_message(self, message: aio_pika.abc.AbstractIncomingMessage) -> None:
        try:
            payload = json.loads(message.body.decode("utf-8"))
            version = str(payload.get("version", "")).strip()
            weights_bucket = str(payload.get("weights_s3_bucket", settings.S3_MODELS_BUCKET)).strip()
            weights_key = str(payload.get("weights_s3_key", "")).strip()
            calib_bucket = str(payload.get("calibration_s3_bucket", settings.S3_MODELS_BUCKET)).strip()
            calib_key = str(payload.get("calibration_s3_key", "")).strip()

            if not version or not weights_key or not calib_key:
                logger.warning("Ignoring incomplete model.reload message: %s", payload)
                await message.ack()
                return

            if not s3_downloader.is_configured:
                logger.warning(
                    "Received model.reload for version='%s', but S3_ENDPOINT is not configured; cannot download new weights.",
                    version,
                )
                await message.ack()
                return

            target_dir = inference_engine.hot_reload_dir / version
            weights_dest = target_dir / "model.pth"
            calib_dest = target_dir / "calibration_cache.json"

            await asyncio.to_thread(s3_downloader.download_file, weights_bucket, weights_key, weights_dest)
            await asyncio.to_thread(s3_downloader.download_file, calib_bucket, calib_key, calib_dest)
            await asyncio.to_thread(inference_engine.reload_from_paths, version, weights_dest, calib_dest)
            await message.ack()
            logger.info("Worker hot-reloaded cleanly to ModelVersion '%s'", version)
        except Exception:
            logger.exception("Failed handling model.reload event")
            await message.nack(requeue=False)


mq_consumer = RabbitMqInferenceConsumer()
