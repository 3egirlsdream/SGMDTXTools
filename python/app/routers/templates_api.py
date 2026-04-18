from __future__ import annotations

import logging
from typing import Any, Dict, List, Optional

from fastapi import APIRouter, HTTPException
from fastapi.responses import FileResponse
from pydantic import BaseModel

logger = logging.getLogger(__name__)
router = APIRouter()


class TemplateCreateRequest(BaseModel):
    name: str
    category: str = "other"
    threshold: float = 0.80
    description: str = ""
    screenshot: str
    region: Dict[str, int]


class TemplateUpdateRequest(BaseModel):
    category: Optional[str] = None
    threshold: Optional[float] = None
    description: Optional[str] = None


class TemplateTestRequest(BaseModel):
    screenshot: str


class TemplateResponse(BaseModel):
    name: str
    file: str
    category: str
    threshold: float
    description: str
    source_screenshot: str
    source_region: Dict[str, Any]


class TemplateTestResult(BaseModel):
    success: bool
    matches: List[Dict[str, Any]] = []
    error: Optional[str] = None


def _get_deps():
    from app.main import get_template_store, get_template_matcher
    from app.config import get_settings
    return get_template_store(), get_template_matcher(), get_settings()


@router.get("/api/templates")
async def list_templates():
    store, _, _ = _get_deps()
    items = []
    for t in store.list_all():
        items.append({
            "name": t.name,
            "file": t.file,
            "category": t.category,
            "threshold": t.threshold,
            "description": t.description,
            "image_url": f"/api/templates/{t.name}/image",
        })
    return {"templates": items, "count": len(items)}


@router.get("/api/templates/{name}")
async def get_template(name: str):
    store, _, _ = _get_deps()
    info = store.get(name)
    if info is None:
        raise HTTPException(status_code=404, detail=f"Template '{name}' not found")
    return TemplateResponse(
        name=info.name,
        file=info.file,
        category=info.category,
        threshold=info.threshold,
        description=info.description,
        source_screenshot=info.source_screenshot,
        source_region=info.source_region,
    )


@router.get("/api/templates/{name}/image")
async def get_template_image(name: str):
    store, _, _ = _get_deps()
    path = store.get_image_path(name)
    if path is None:
        raise HTTPException(status_code=404, detail=f"Template image '{name}' not found")
    return FileResponse(str(path), media_type="image/png")


@router.post("/api/templates")
async def create_template(req: TemplateCreateRequest):
    store, matcher, settings = _get_deps()
    from pathlib import Path
    screenshot_path = Path(settings.screenshots_dir) / req.screenshot
    if not screenshot_path.exists():
        raise HTTPException(status_code=404, detail=f"Screenshot not found: {req.screenshot}")

    try:
        info = store.create_from_screenshot(
            name=req.name,
            screenshot_path=str(screenshot_path),
            region=req.region,
            category=req.category,
            threshold=req.threshold,
            description=req.description,
        )
        matcher.reload()
        return {"success": True, "template": info.name}
    except ValueError as e:
        raise HTTPException(status_code=400, detail=str(e))


@router.put("/api/templates/{name}")
async def update_template(name: str, req: TemplateUpdateRequest):
    store, _, _ = _get_deps()
    try:
        kwargs = {k: v for k, v in req.dict().items() if v is not None}
        info = store.update(name, **kwargs)
        return {"success": True, "template": info.name}
    except ValueError as e:
        raise HTTPException(status_code=404, detail=str(e))


@router.delete("/api/templates/{name}")
async def delete_template(name: str):
    store, matcher, _ = _get_deps()
    try:
        store.delete(name)
        matcher.reload()
        return {"success": True}
    except ValueError as e:
        raise HTTPException(status_code=404, detail=str(e))


@router.post("/api/templates/{name}/test")
async def test_template(name: str, req: TemplateTestRequest):
    store, matcher, settings = _get_deps()
    info = store.get(name)
    if info is None:
        raise HTTPException(status_code=404, detail=f"Template '{name}' not found")

    from pathlib import Path
    import cv2
    screenshot_path = Path(settings.screenshots_dir) / req.screenshot
    if not screenshot_path.exists():
        raise HTTPException(status_code=404, detail=f"Screenshot not found: {req.screenshot}")

    img = cv2.imread(str(screenshot_path))
    if img is None:
        return TemplateTestResult(success=False, error="Failed to read screenshot")

    matches = matcher.match(img, [name])
    results = []
    for m in matches:
        results.append({
            "confidence": m.confidence,
            "bbox": m.bbox.dict(),
            "center": m.center.dict(),
        })
    return TemplateTestResult(success=True, matches=results)
