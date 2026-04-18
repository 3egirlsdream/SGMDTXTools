from __future__ import annotations

import logging
from pathlib import Path
from typing import Any, Dict, List, Optional

import cv2
import numpy as np
import yaml
from app.models.schemas import BBox, Point

logger = logging.getLogger(__name__)


class TemplateInfo:
    def __init__(
        self,
        name: str,
        file: str,
        category: str = "other",
        threshold: float = 0.80,
        description: str = "",
        source_screenshot: str = "",
        source_region: Optional[Dict[str, int]] = None,
    ):
        self.name = name
        self.file = file
        self.category = category
        self.threshold = threshold
        self.description = description
        self.source_screenshot = source_screenshot
        self.source_region = source_region or {}

    def to_dict(self) -> Dict[str, Any]:
        d: Dict[str, Any] = {
            "name": self.name,
            "file": self.file,
            "category": self.category,
            "threshold": self.threshold,
            "description": self.description,
        }
        if self.source_screenshot:
            d["source_screenshot"] = self.source_screenshot
        if self.source_region:
            d["source_region"] = self.source_region
        return d


class TemplateStore:
    def __init__(self, templates_dir: str):
        self.templates_dir = Path(templates_dir)
        self.yaml_path = self.templates_dir / "templates.yaml"
        self._templates: Dict[str, TemplateInfo] = {}
        self._load()

    def _load(self) -> None:
        self._templates.clear()
        if not self.yaml_path.exists():
            self._save_yaml()
            return
        with open(self.yaml_path, "r", encoding="utf-8") as f:
            data = yaml.safe_load(f) or {}
        for item in data.get("templates", []):
            info = TemplateInfo(**item)
            self._templates[info.name] = info
        logger.info("Loaded %d templates from %s", len(self._templates), self.yaml_path)

    def _save_yaml(self) -> None:
        data = {"templates": [t.to_dict() for t in self._templates.values()]}
        self.templates_dir.mkdir(parents=True, exist_ok=True)
        with open(self.yaml_path, "w", encoding="utf-8") as f:
            yaml.dump(data, f, allow_unicode=True, default_flow_style=False, sort_keys=False)

    def list_all(self) -> List[TemplateInfo]:
        return list(self._templates.values())

    def get(self, name: str) -> Optional[TemplateInfo]:
        return self._templates.get(name)

    def get_image_path(self, name: str) -> Optional[Path]:
        info = self._templates.get(name)
        if info is None:
            return None
        p = self.templates_dir / info.file
        return p if p.exists() else None

    def create(
        self,
        name: str,
        image_data: bytes,
        category: str = "other",
        threshold: float = 0.80,
        description: str = "",
        source_screenshot: str = "",
        source_region: Optional[Dict[str, int]] = None,
    ) -> TemplateInfo:
        if name in self._templates:
            raise ValueError(f"Template '{name}' already exists")

        filename = f"{name}.png"
        filepath = self.templates_dir / filename

        arr = np.frombuffer(image_data, dtype=np.uint8)
        img = cv2.imdecode(arr, cv2.IMREAD_COLOR)
        if img is None:
            raise ValueError("Failed to decode image data")
        cv2.imwrite(str(filepath), img)

        info = TemplateInfo(
            name=name,
            file=filename,
            category=category,
            threshold=threshold,
            description=description,
            source_screenshot=source_screenshot,
            source_region=source_region,
        )
        self._templates[name] = info
        self._save_yaml()
        logger.info("Created template '%s' (%s)", name, filename)
        return info

    def create_from_screenshot(
        self,
        name: str,
        screenshot_path: str,
        region: Dict[str, int],
        category: str = "other",
        threshold: float = 0.80,
        description: str = "",
    ) -> TemplateInfo:
        img = cv2.imread(screenshot_path)
        if img is None:
            raise ValueError(f"Failed to read screenshot: {screenshot_path}")

        x, y, w, h = region["x"], region["y"], region["width"], region["height"]
        cropped = img[y : y + h, x : x + w]
        if cropped.size == 0:
            raise ValueError(f"Crop region is empty: {region}")

        filename = f"{name}.png"
        filepath = self.templates_dir / filename
        cv2.imwrite(str(filepath), cropped)

        screenshot_name = Path(screenshot_path).name
        info = TemplateInfo(
            name=name,
            file=filename,
            category=category,
            threshold=threshold,
            description=description,
            source_screenshot=screenshot_name,
            source_region=region,
        )
        self._templates[name] = info
        self._save_yaml()
        logger.info("Created template '%s' from screenshot crop", name)
        return info

    def update(self, name: str, **kwargs: Any) -> TemplateInfo:
        info = self._templates.get(name)
        if info is None:
            raise ValueError(f"Template '{name}' not found")
        for key, value in kwargs.items():
            if hasattr(info, key) and key not in ("name", "file"):
                setattr(info, key, value)
        self._save_yaml()
        return info

    def delete(self, name: str) -> None:
        info = self._templates.get(name)
        if info is None:
            raise ValueError(f"Template '{name}' not found")
        filepath = self.templates_dir / info.file
        if filepath.exists():
            filepath.unlink()
        del self._templates[name]
        self._save_yaml()
        logger.info("Deleted template '%s'", name)

    def reload(self) -> None:
        self._load()

    @property
    def count(self) -> int:
        return len(self._templates)
