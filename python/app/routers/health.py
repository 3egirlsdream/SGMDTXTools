from fastapi import APIRouter
from app.models.schemas import HealthResponse
from app.services import ocr_engine

router = APIRouter()


@router.get("/api/health", response_model=HealthResponse)
async def health():
    from app.main import get_template_store
    try:
        count = get_template_store().count
    except Exception:
        count = 0
    return HealthResponse(
        status="ok",
        version="0.1.0",
        ocr_ready=ocr_engine.is_ready(),
        template_count=count,
    )
