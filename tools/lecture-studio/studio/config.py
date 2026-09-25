"""Configuration: defaults, optional JSON override file and environment variables.

Nothing secret lives here. YouTube OAuth credentials are read from the environment only (see youtube.py).
"""
from __future__ import annotations

import json
import os
from dataclasses import dataclass, field
from pathlib import Path

TOOL_DIR = Path(__file__).resolve().parent.parent
REPO_ROOT = TOOL_DIR.parent.parent

# Voice per course category (owner decision, 2026-09). voice_ids come from creative_list_voices.
VOICE_JACOB = "SO9JediIwzugrikv7xw0"
VOICE_VANESSA = "9BYUod15YOH2aePSd97v"
VOICE_DANIEL = "ynTHHllfDmGsZ3G8QJt9"
DEFAULT_VOICES = {
    "ai": VOICE_JACOB,
    "data": VOICE_JACOB,
    "platform": VOICE_JACOB,
    "marketing": VOICE_VANESSA,
    "seo": VOICE_VANESSA,
    "design": VOICE_VANESSA,
    "sales": VOICE_DANIEL,
    "business": VOICE_DANIEL,
}
VOICE_NAMES = {VOICE_JACOB: "Jacob L.", VOICE_VANESSA: "Vanessa", VOICE_DANIEL: "Daniel"}


def _default_catalog() -> Path:
    env = os.environ.get("LECTURE_STUDIO_CATALOG")
    if env:
        return Path(env)
    return REPO_ROOT / "backend/src/OptimizeAll.Api/Modules/Learning/Catalog"


def _default_work() -> Path:
    return Path(os.environ.get("LECTURE_STUDIO_WORK", str(TOOL_DIR / ".work")))


def _default_node_modules() -> Path:
    env = os.environ.get("LECTURE_STUDIO_NODE_MODULES")
    if env:
        return Path(env)
    return REPO_ROOT / "frontend/node_modules"


@dataclass
class Config:
    catalog_dir: Path = field(default_factory=_default_catalog)
    work_dir: Path = field(default_factory=_default_work)
    node_modules: Path = field(default_factory=_default_node_modules)
    site_base_url: str = "https://www.optimizeall.com"
    model_id: str = "eleven_multilingual_v2"
    voices: dict = field(default_factory=lambda: dict(DEFAULT_VOICES))
    fallback_voice: str = VOICE_JACOB
    width: int = 1920
    height: int = 1080
    fps: int = 30
    lead_in: float = 0.45  # seconds of picture before the narration starts in each scene
    tail: float = 0.75  # seconds after the narration ends (breathing room + exit animation)
    intro_seconds: float = 3.0
    outro_seconds: float = 6.0
    loudness_lufs: float = -16.0
    true_peak: float = -1.5
    render_workers: int = 3
    max_output_bytes: int = 3 * 1024**3  # keep local outputs under 3 GB
    youtube_privacy: str = "unlisted"
    youtube_category_id: str = "27"  # Education
    youtube_language: str = "en"

    @property
    def cache_dir(self) -> Path:
        return self.work_dir / "cache"

    @property
    def audio_cache(self) -> Path:
        return self.cache_dir / "tts"

    def voice_for(self, category: str | None) -> str:
        return self.voices.get((category or "").lower(), self.fallback_voice)

    def lesson_url(self, course: str, lesson: str) -> str:
        return f"{self.site_base_url.rstrip('/')}/learn/{course}/{lesson}"

    @classmethod
    def load(cls, path: str | os.PathLike | None = None) -> "Config":
        cfg = cls()
        p = Path(path) if path else TOOL_DIR / "studio.config.json"
        if p.exists():
            data = json.loads(p.read_text(encoding="utf-8"))
            for key, value in data.items():
                if key.startswith("_"):
                    continue
                if not hasattr(cfg, key):
                    raise ValueError(f"unknown config key {key!r} in {p}")
                if key in ("catalog_dir", "work_dir", "node_modules"):
                    value = (p.parent / value).resolve() if not os.path.isabs(value) else Path(value)
                setattr(cfg, key, value)
        if os.environ.get("LECTURE_STUDIO_SITE"):
            cfg.site_base_url = os.environ["LECTURE_STUDIO_SITE"]
        if os.environ.get("LECTURE_STUDIO_WORK"):
            cfg.work_dir = Path(os.environ["LECTURE_STUDIO_WORK"])
        return cfg
