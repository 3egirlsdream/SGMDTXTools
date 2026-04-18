from __future__ import annotations

import json
import time
import logging
from typing import Optional

import cv2
import numpy as np
from fastapi import APIRouter, File, Form, UploadFile
from app.models.schemas import OcrResponse, ImageSize
from app.services import ocr_engine

logger = logging.getLogger(__name__)
router = APIRouter()


@router.post("/api/ocr", response_model=OcrResponse)
async def ocr(
    image: UploadFile = File(...),
    region: Optional[str] = Form(None),
):
    start = time.perf_counter()

    try:
        data = await image.read()
        arr = np.frombuffer(data, dtype=np.uint8)
        img = cv2.imdecode(arr, cv2.IMREAD_COLOR)
        if img is None:
            return OcrResponse(success=False, elapsed_ms=0, error="Failed to decode image")

        h, w = img.shape[:2]

        if region:
            r = json.loads(region)
            texts = ocr_engine.detect_region(
                img, int(r["x"]), int(r["y"]), int(r["width"]), int(r["height"])
            )
        else:
            texts = ocr_engine.detect(img)

        elapsed = int((time.perf_counter() - start) * 1000)
        return OcrResponse(
            success=True,
            elapsed_ms=elapsed,
            image_size=ImageSize(width=w, height=h),
            texts=texts,
        )
    except Exception as e:
        elapsed = int((time.perf_counter() - start) * 1000)
        logger.exception("OCR failed")
        return OcrResponse(success=False, elapsed_ms=elapsed, error=str(e))
