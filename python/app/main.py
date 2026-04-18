import logging
from contextlib import asynccontextmanager
from fastapi import FastAPI
from fastapi.staticfiles import StaticFiles
from fastapi.responses import FileResponse
from pathlib import Path
from app.config import get_settings
from app.services import ocr_engine
from app.services.template_store import TemplateStore
from app.services.template_matcher import TemplateMatcher

logger = logging.getLogger(__name__)

_template_store = None
_template_matcher = None


def get_template_store() -> TemplateStore:
    assert _template_store is not None
    return _template_store


def get_template_matcher() -> TemplateMatcher:
    assert _template_matcher is not None
    return _template_matcher


@asynccontextmanager
async def lifespan(app: FastAPI):
    global _template_store, _template_matcher

    settings = get_settings()
    logging.basicConfig(
        level=getattr(logging, settings.log_level.upper(), logging.INFO),
        format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
    )
    logger.info("Starting screen parser service on %s:%d", settings.host, settings.port)

    ocr_engine.init_ocr(
        lang=settings.ocr_lang,
        use_gpu=settings.ocr_use_gpu,
        det_db_thresh=settings.ocr_det_db_thresh,
        rec_batch_num=settings.ocr_rec_batch_num,
    )

    _template_store = TemplateStore(settings.templates_dir)
    _template_matcher = TemplateMatcher(_template_store)
    logger.info("Template store loaded: %d templates", _template_store.count)

    from app.routers import match as match_router, scan as scan_router
    match_router.set_matcher(_template_matcher)
    scan_router.set_matcher(_template_matcher)

    yield

    logger.info("Shutting down screen parser service.")


app = FastAPI(title="SGMDTXTools Screen Parser", version="0.1.0", lifespan=lifespan)

from app.routers import health, ocr, match, scan, templates_api, screenshots_api
app.include_router(health.router)
app.include_router(ocr.router)
app.include_router(match.router)
app.include_router(scan.router)
app.include_router(templates_api.router)
app.include_router(screenshots_api.router)

static_dir = Path(__file__).parent / "static"
if static_dir.exists():
    app.mount("/static", StaticFiles(directory=str(static_dir)), name="static")

    @app.get("/")
    async def serve_ui():
        index = static_dir / "index.html"
        if index.exists():
            return FileResponse(str(index))
        return {"message": "Template management UI not yet available."}
