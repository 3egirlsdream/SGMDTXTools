from __future__ import annotations

import time
import logging

import cv2
import numpy as np
from fastapi import APIRouter, File, UploadFile
from app.models.schemas import ScanResponse, ImageSize
from app.services import ocr_engine

logger = logging.getLogger(__name__)
router = APIRouter()

_matcher = None


def set_matcher(matcher) -> None:
    global _matcher
    _matcher = matcher


@router.post("/api/scan", response_model=ScanResponse)
async def scan(
    image: UploadFile = File(...),
):
    start = time.perf_counter()

    try:
        data = await image.read()
        arr = np.frombuffer(data, dtype=np.uint8)
        img = cv2.imdecode(arr, cv2.IMREAD_COLOR)
        if img is None:
            return ScanResponse(success=False, elapsed_ms=0, error="Failed to decode image")

        h, w = img.shape[:2]

        texts = ocr_engine.detect(img)

        matches = []
        if _matcher is not None:
            matches = _matcher.match_all(img)

        elapsed = int((time.perf_counter() - start) * 1000)
        return ScanResponse(
            success=True,
            elapsed_ms=elapsed,
            image_size=ImageSize(width=w, height=h),
            texts=texts,
            matches=matches,
        )
    except Exception as e:
        elapsed = int((time.perf_counter() - start) * 1000)
        logger.exception("Scan failed")
        return ScanResponse(success=False, elapsed_ms=elapsed, error=str(e))
