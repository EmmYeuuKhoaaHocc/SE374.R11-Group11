from typing import Optional
from pydantic import BaseModel


class HealthResponse(BaseModel):
    status: str
    model_loaded: bool
    device: str
    weights_path: str
    rabbitmq_enabled: bool = False
    rabbitmq_connected: bool = False
    rabbitmq_error: Optional[str] = None
