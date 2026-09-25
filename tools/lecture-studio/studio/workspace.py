"""Per-lecture working directory layout (all under cfg.work_dir, never committed)."""
from __future__ import annotations

import json
import os
import tempfile
from pathlib import Path

from .config import Config


class LectureDir:
    def __init__(self, cfg: Config, key: str):
        course, lesson = key.split("/", 1)
        self.key = key
        self.root = cfg.work_dir / "lectures" / course / lesson
        self.root.mkdir(parents=True, exist_ok=True)

    plan = property(lambda self: self.root / "plan.json")
    narration = property(lambda self: self.root / "narration.json")
    timeline = property(lambda self: self.root / "timeline.json")
    segments = property(lambda self: self.root / "segments")
    out = property(lambda self: self.root / "out")

    def output(self, name: str) -> Path:
        self.out.mkdir(parents=True, exist_ok=True)
        return self.out / name


def write_json(path: Path, data) -> None:
    """Atomic JSON write (a crash never leaves a half-written ledger/plan)."""
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, tmp = tempfile.mkstemp(dir=path.parent, prefix=path.name, suffix=".tmp")
    with os.fdopen(fd, "w", encoding="utf-8") as fh:
        json.dump(data, fh, indent=2, ensure_ascii=False)
        fh.write("\n")
    os.replace(tmp, path)


def read_json(path: Path, default=None):
    if not path.exists():
        return default
    return json.loads(path.read_text(encoding="utf-8"))
