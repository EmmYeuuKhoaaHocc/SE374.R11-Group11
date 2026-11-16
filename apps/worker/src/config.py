from pathlib import Path
from pydantic_settings import BaseSettings

class Settings(BaseSettings):
    APP_NAME: str = "Brain Tumor 3D UNet Worker"
    HOST: str = "0.0.0.0"
    PORT: int = 8000
    DEBUG: bool = False
    MODEL_WEIGHTS_PATH: str = str(Path(__file__).resolve().parent.parent / "weights" / "unet3d_final_fp32.pth")
    DEVICE: str = "cuda"
    PATCH_DEPTH: int = 128
    PATCH_HEIGHT: int = 128
    PATCH_WIDTH: int = 128
    OVERLAP: float = 0.5
    MC_NUM_PASSES: int = 20
    MC_BATCH_SIZE: int = 5
    QUANTILE_CHUNK_SIZE: int = 250000
    CORS_ORIGINS: list[str] = ["*"]

    # RabbitMQ connection (optional; consumer starts only when RABBITMQ_HOST is provided)
    RABBITMQ_HOST: str = ""
    RABBITMQ_PORT: int = 5672
    RABBITMQ_USER: str = ""
    RABBITMQ_PASS: str = ""
    RABBITMQ_VHOST: str = "/"
    RABBITMQ_EXCHANGE: str = "neuroscan.inference"
    RABBITMQ_REQUEST_QUEUE: str = "neuroscan.inference.requests"
    RABBITMQ_REQUEST_ROUTING_KEY: str = "inference.request"
    RABBITMQ_MAX_RETRIES: int = 3
    RABBITMQ_RETRY_DELAY_MS: int = 5000

    model_config = {
        "env_file": ".env",
        "env_file_encoding": "utf-8",
        "extra": "ignore"
    }

settings = Settings()

