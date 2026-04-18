from __future__ import annotations

import logging
from pathlib import Path
from typing import Dict, List, Optional, Tuple

import cv2
import numpy as np
from app.models.schemas import MatchItem, BBox, Point
from app.services.template_store import TemplateStore

logger = logging.getLogger(__name__)


class TemplateMatcher:
    def __init__(self, store: TemplateStore):
        self._store = store
        self._cache: Dict[str, np.ndarray] = {}
        self._load_images()

    def _load_images(self) -> None:
        self._cache.clear()
        for info in self._store.list_all():
            img_path = self._store.get_image_path(info.name)
            if img_path is not None:
                img = cv2.imread(str(img_path), cv2.IMREAD_COLOR)
                if img is not None:
                    self._cache[info.name] = img
                else:
                    logger.warning("Failed to load template image: %s", img_path)
        logger.info("Loaded %d template images into cache", len(self._cache))

    def reload(self) -> None:
        self._store.reload()
        self._load_images()

    def match_all(self, image: np.ndarray) -> List[MatchItem]:
        results: List[MatchItem] = []
        for info in self._store.list_all():
            template_img = self._cache.get(info.name)
            if template_img is None:
                continue
            matches = self._match_template(image, template_img, info.name, info.threshold)
            results.extend(matches)
        return results

    def match(self, image: np.ndarray, template_names: List[str]) -> List[MatchItem]:
        results: List[MatchItem] = []
        for name in template_names:
            info = self._store.get(name)
            if info is None:
                continue
            template_img = self._cache.get(name)
            if template_img is None:
                continue
            matches = self._match_template(image, template_img, name, info.threshold)
            results.extend(matches)
        return results

    def _match_template(
        self,
        image: np.ndarray,
        template: np.ndarray,
        name: str,
        threshold: float,
    ) -> List[MatchItem]:
        th, tw = template.shape[:2]
        ih, iw = image.shape[:2]

        all_boxes: List[Tuple[int, int, int, int, float]] = []
        scales = [0.85, 0.9, 0.95, 1.0, 1.05, 1.1, 1.15]

        for scale in scales:
            sw = int(tw * scale)
            sh = int(th * scale)
            if sw <= 0 or sh <= 0 or sw > iw or sh > ih:
                continue

            if scale != 1.0:
                scaled = cv2.resize(template, (sw, sh), interpolation=cv2.INTER_LINEAR)
            else:
                scaled = template

            result = cv2.matchTemplate(image, scaled, cv2.TM_CCOEFF_NORMED)
            locations = np.where(result >= threshold)

            for pt_y, pt_x in zip(*locations):
                conf = float(result[pt_y, pt_x])
                all_boxes.append((int(pt_x), int(pt_y), sw, sh, conf))

        filtered = self._nms(all_boxes, iou_threshold=0.3)

        items: List[MatchItem] = []
        for (bx, by, bw, bh, conf) in filtered:
            items.append(MatchItem(
                template=name,
                confidence=round(conf, 4),
                bbox=BBox(x=bx, y=by, width=bw, height=bh),
                center=Point(x=bx + bw // 2, y=by + bh // 2),
            ))
        return items

    @staticmethod
    def _nms(
        boxes: List[Tuple[int, int, int, int, float]],
        iou_threshold: float = 0.3,
    ) -> List[Tuple[int, int, int, int, float]]:
        if not boxes:
            return []

        sorted_boxes = sorted(boxes, key=lambda b: b[4], reverse=True)
        keep: List[Tuple[int, int, int, int, float]] = []

        while sorted_boxes:
            best = sorted_boxes.pop(0)
            keep.append(best)
            remaining: List[Tuple[int, int, int, int, float]] = []
            for box in sorted_boxes:
                if _iou(best, box) < iou_threshold:
                    remaining.append(box)
            sorted_boxes = remaining

        return keep


def _iou(
    a: Tuple[int, int, int, int, float],
    b: Tuple[int, int, int, int, float],
) -> float:
    ax1, ay1, aw, ah, _ = a
    bx1, by1, bw, bh, _ = b
    ax2, ay2 = ax1 + aw, ay1 + ah
    bx2, by2 = bx1 + bw, by1 + bh

    ix1 = max(ax1, bx1)
    iy1 = max(ay1, by1)
    ix2 = min(ax2, bx2)
    iy2 = min(ay2, by2)

    if ix2 <= ix1 or iy2 <= iy1:
        return 0.0

    inter = (ix2 - ix1) * (iy2 - iy1)
    union = aw * ah + bw * bh - inter
    return inter / union if union > 0 else 0.0
