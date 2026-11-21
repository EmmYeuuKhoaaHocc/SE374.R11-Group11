from fastapi import APIRouter

from .config import settings
from .inference import inference_engine
from .mq_consumer import mq_consumer
from .schemas import HealthResponse

router = APIRouter()


@router.get("/health", response_model=HealthResponse)
async def health_check():
    return HealthResponse(
        status="healthy" if inference_engine.model is not None else "degraded",
        model_loaded=inference_engine.model is not None,
        model_version=inference_engine.current_version,
        device=str(inference_engine.device),
        weights_path=inference_engine.model_manager.weights_path or settings.MODEL_WEIGHTS_PATH,
        rabbitmq_enabled=mq_consumer.is_enabled,
        rabbitmq_connected=mq_consumer.connected,
        rabbitmq_error=mq_consumer.last_error,
    )
