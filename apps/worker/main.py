import argparse
import logging
import os
from contextlib import asynccontextmanager

from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
import uvicorn

from src.api import router
from src.config import settings
from src.inference import inference_engine
from src.mq_consumer import mq_consumer

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
)


@asynccontextmanager
async def lifespan(app: FastAPI):
    inference_engine.load_model()
    await mq_consumer.start()
    try:
        yield
    finally:
        await mq_consumer.stop()


app = FastAPI(
    title=settings.APP_NAME,
    lifespan=lifespan
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=settings.CORS_ORIGINS,
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

app.include_router(router)


def _apply_cli_args() -> None:
    parser = argparse.ArgumentParser(description="NeuroScan AI Worker")
    parser.add_argument("--host", default=None)
    parser.add_argument("--port", type=int, default=None)
    parser.add_argument("--mc-batch-size", type=int, default=None)
    parser.add_argument("--rmq-host", default=None)
    parser.add_argument("--rmq-port", type=int, default=None)
    parser.add_argument("--rmq-user", default=None)
    parser.add_argument("--rmq-pass", default=None)
    parser.add_argument("--rmq-vhost", default=None)
    parser.add_argument("--rmq-exchange", default=None)
    args, _ = parser.parse_known_args()

    mapping = {
        "HOST": args.host,
        "PORT": args.port,
        "MC_BATCH_SIZE": args.mc_batch_size,
        "RABBITMQ_HOST": args.rmq_host,
        "RABBITMQ_PORT": args.rmq_port,
        "RABBITMQ_USER": args.rmq_user,
        "RABBITMQ_PASS": args.rmq_pass,
        "RABBITMQ_VHOST": args.rmq_vhost,
        "RABBITMQ_EXCHANGE": args.rmq_exchange,
    }
    for key, value in mapping.items():
        if value is not None:
            setattr(settings, key, value)
            os.environ[key] = str(value)


if __name__ == "__main__":
    _apply_cli_args()
    uvicorn.run(
        "main:app",
        host=settings.HOST,
        port=settings.PORT,
        reload=settings.DEBUG
    )

