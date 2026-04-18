from functools import lru_cache
from pathlib import Path
from pydantic_settings import BaseSettings


class Settings(BaseSettings):
    host: str = "127.0.0.1"
    port: int = 5100
    log_level: str = "INFO"

    ocr_lang: str = "ch"
    ocr_use_gpu: bool = False
    ocr_det_db_thresh: float = 0.3
    ocr_rec_batch_num: int = 6

    templates_dir: str = str(Path(__file__).resolve().parent.parent / "templates")
    screenshots_dir: str = str(
        Path(__file__).resolve().parent.parent.parent / "screenshots"
    )
    max_image_size_mb: int = 10

    model_config = {"env_prefix": "SGMDTX_", "env_file": ".env"}


@lru_cache
def get_settings() -> Settings:
    return Settings()
