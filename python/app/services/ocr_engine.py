import logging
import numpy as np
from app.models.schemas import TextItem, BBox, Point

logger = logging.getLogger(__name__)

_ocr_instance = None
_ocr_ready = False
_engine_type = "rapidocr"  # "rapidocr" or "paddleocr"


def init_ocr(lang: str = "ch", use_gpu: bool = False, det_db_thresh: float = 0.3,
             rec_batch_num: int = 6) -> None:
    global _ocr_instance, _ocr_ready, _engine_type

    # 优先使用 RapidOCR (ONNX Runtime, CPU 上快 7-8x)
    try:
        from rapidocr_onnxruntime import RapidOCR
        logger.info("Loading RapidOCR (ONNX Runtime)...")
        _ocr_instance = RapidOCR()
        _engine_type = "rapidocr"
        _ocr_ready = True
        logger.info("RapidOCR loaded successfully.")
        return
    except ImportError:
        logger.info("RapidOCR not available, falling back to PaddleOCR...")

    # 回退到 PaddleOCR
    import os
    os.environ["PADDLE_PDX_DISABLE_MODEL_SOURCE_CHECK"] = "True"

    from paddleocr import PaddleOCR
    logger.info("Loading PaddleOCR model (lang=%s)...", lang)
    _ocr_instance = PaddleOCR(
        lang=lang,
        use_doc_orientation_classify=False,
        use_doc_unwarping=False,
        use_textline_orientation=True,
        text_det_box_thresh=det_db_thresh,
        text_recognition_batch_size=rec_batch_num,
    )
    _engine_type = "paddleocr"
    _ocr_ready = True
    logger.info("PaddleOCR model loaded successfully.")


def is_ready() -> bool:
    return _ocr_ready


def _ensure_rgb(image: np.ndarray) -> np.ndarray:
    """Convert RGBA/grayscale to RGB."""
    if image.ndim == 2:
        return np.stack([image] * 3, axis=-1)
    if image.shape[2] == 4:
        return image[:, :, :3]
    return image


def _detect_rapidocr(image: np.ndarray, min_confidence: float) -> list[TextItem]:
    result, _ = _ocr_instance(image)
    items: list[TextItem] = []
    if not result:
        return items
    for box, text, score in result:
        if score < min_confidence:
            continue
        # box is [[x1,y1],[x2,y2],[x3,y3],[x4,y4]]
        xs = [p[0] for p in box]
        ys = [p[1] for p in box]
        x_min = int(min(xs))
        y_min = int(min(ys))
        x_max = int(max(xs))
        y_max = int(max(ys))
        w = x_max - x_min
        h = y_max - y_min
        items.append(TextItem(
            text=text,
            confidence=round(float(score), 4),
            bbox=BBox(x=x_min, y=y_min, width=w, height=h),
            center=Point(x=x_min + w // 2, y=y_min + h // 2),
        ))
    return items


def _detect_paddleocr(image: np.ndarray, min_confidence: float) -> list[TextItem]:
    items: list[TextItem] = []
    for result in _ocr_instance.predict(image):
        rec_texts = result.get('rec_texts', [])
        rec_scores = result.get('rec_scores', [])
        dt_polys = result.get('dt_polys', [])
        if not rec_texts:
            continue
        for i, (text, score) in enumerate(zip(rec_texts, rec_scores)):
            if score < min_confidence:
                continue
            poly = dt_polys[i]
            xs = [p[0] for p in poly]
            ys = [p[1] for p in poly]
            x_min = int(min(xs))
            y_min = int(min(ys))
            x_max = int(max(xs))
            y_max = int(max(ys))
            w = x_max - x_min
            h = y_max - y_min
            items.append(TextItem(
                text=text,
                confidence=round(float(score), 4),
                bbox=BBox(x=x_min, y=y_min, width=w, height=h),
                center=Point(x=x_min + w // 2, y=y_min + h // 2),
            ))
    return items


def detect(image: np.ndarray, min_confidence: float = 0.5) -> list[TextItem]:
    if _ocr_instance is None:
        raise RuntimeError("OCR engine not initialized. Call init_ocr() first.")

    image = _ensure_rgb(image)

    try:
        if _engine_type == "rapidocr":
            return _detect_rapidocr(image, min_confidence)
        else:
            return _detect_paddleocr(image, min_confidence)
    except Exception as e:
        logger.error("OCR detection failed: %s", e)
        raise


def detect_region(image: np.ndarray, x: int, y: int, w: int, h: int,
                  min_confidence: float = 0.5) -> list[TextItem]:
    cropped = image[y:y + h, x:x + w]
    items = detect(cropped, min_confidence)
    for item in items:
        item.bbox.x += x
        item.bbox.y += y
        item.center.x += x
        item.center.y += y
    return items
