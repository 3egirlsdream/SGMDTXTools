from typing import List, Optional

from pydantic import BaseModel


class BBox(BaseModel):
    x: int
    y: int
    width: int
    height: int


class Point(BaseModel):
    x: int
    y: int


class ImageSize(BaseModel):
    width: int
    height: int


class TextItem(BaseModel):
    text: str
    confidence: float
    bbox: BBox
    center: Point


class MatchItem(BaseModel):
    template: str
    confidence: float
    bbox: BBox
    center: Point


class HealthResponse(BaseModel):
    status: str
    version: str
    ocr_ready: bool
    template_count: int


class OcrResponse(BaseModel):
    success: bool
    elapsed_ms: int
    image_size: Optional[ImageSize] = None
    texts: List[TextItem] = []
    error: Optional[str] = None


class MatchResponse(BaseModel):
    success: bool
    elapsed_ms: int
    matches: List[MatchItem] = []
    error: Optional[str] = None


class ScanResponse(BaseModel):
    success: bool
    elapsed_ms: int
    image_size: Optional[ImageSize] = None
    texts: List[TextItem] = []
    matches: List[MatchItem] = []
    error: Optional[str] = None
