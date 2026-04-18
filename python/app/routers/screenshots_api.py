from __future__ import annotations

import os
import logging
from pathlib import Path
from typing import List

from fastapi import APIRouter, HTTPException
from fastapi.responses import FileResponse

logger = logging.getLogger(__name__)
router = APIRouter()


def _get_screenshots_dir() -> Path:
    from app.config import get_settings
    return Path(get_settings().screenshots_dir)


@router.get("/api/screenshots")
async def list_screenshots():
    screenshots_dir = _get_screenshots_dir()
    if not screenshots_dir.exists():
        return {"screenshots": [], "count": 0}

    files = []
    for f in sorted(screenshots_dir.iterdir(), key=lambda p: p.stat().st_mtime, reverse=True):
        if f.suffix.lower() in (".png", ".jpg", ".jpeg"):
            stat = f.stat()
            files.append({
                "filename": f.name,
                "size_bytes": stat.st_size,
                "modified": stat.st_mtime,
                "url": f"/api/screenshots/{f.name}",
            })
    return {"screenshots": files, "count": len(files)}


@router.get("/api/screenshots/{filename}")
async def get_screenshot(filename: str):
    screenshots_dir = _get_screenshots_dir()
    filepath = screenshots_dir / filename

    if not filepath.exists() or not filepath.is_file():
        raise HTTPException(status_code=404, detail=f"Screenshot not found: {filename}")

    if ".." in filename or "/" in filename or "\\" in filename:
        raise HTTPException(status_code=400, detail="Invalid filename")

    suffix = filepath.suffix.lower()
    media_type = "image/png" if suffix == ".png" else "image/jpeg"
    return FileResponse(str(filepath), media_type=media_type)
