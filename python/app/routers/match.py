from __future__ import annotations

import json
import time
import logging
from typing import List, Optional

import cv2
import numpy as np
from fastapi import APIRouter, File, Form, UploadFile
from app.models.schemas import MatchResponse

logger = logging.getLogger(__name__)
router = APIRouter()

_matcher = None


def set_matcher(matcher) -> None:
    global _matcher
    _matcher = matcher


@router.post("/api/match", response_model=MatchResponse)
async def match(
    image: UploadFile = File(...),
    templates: Optional[str] = Form(None),
):
    start = time.perf_counter()

    if _matcher is None:
        return MatchResponse(success=False, elapsed_ms=0, error="Template matcher not initialized")

    try:
        data = await image.read()
        arr = np.frombuffer(data, dtype=np.uint8)
        img = cv2.imdecode(arr, cv2.IMREAD_COLOR)
        if img is None:
            return MatchResponse(success=False, elapsed_ms=0, error="Failed to decode image")

        if templates:
            template_names = json.loads(templates)
            matches = _matcher.match(img, template_names)
        else:
            matches = _matcher.match_all(img)

        elapsed = int((time.perf_counter() - start) * 1000)
        return MatchResponse(success=True, elapsed_ms=elapsed, matches=matches)
    except Exception as e:
        elapsed = int((time.perf_counter() - start) * 1000)
        logger.exception("Template matching failed")
        return MatchResponse(success=False, elapsed_ms=elapsed, error=str(e))
