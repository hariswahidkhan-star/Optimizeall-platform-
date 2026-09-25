"""ffmpeg helpers: locating ffmpeg, exact durations, silence detection, loudness measurement."""
from __future__ import annotations

import json
import os
import re
import shutil
import subprocess
from functools import lru_cache
from pathlib import Path

SAMPLE_RATE = 48000


@lru_cache(maxsize=1)
def ffmpeg_exe() -> str:
    env = os.environ.get("FFMPEG")
    if env:
        return env
    try:
        import imageio_ffmpeg  # type: ignore

        return imageio_ffmpeg.get_ffmpeg_exe()
    except Exception:  # pragma: no cover - depends on the machine
        found = shutil.which("ffmpeg")
        if not found:
            raise RuntimeError("ffmpeg not found: pip install imageio-ffmpeg or set FFMPEG=/path/to/ffmpeg")
        return found


def run_ffmpeg(args: list[str], *, capture: bool = True, input_bytes: bytes | None = None) -> subprocess.CompletedProcess:
    cmd = [ffmpeg_exe(), "-hide_banner", "-nostdin", *args] if input_bytes is None else [ffmpeg_exe(), "-hide_banner", *args]
    res = subprocess.run(cmd, capture_output=capture, input=input_bytes)
    if res.returncode != 0:
        err = res.stderr.decode("utf-8", "replace")[-2000:] if capture and res.stderr else ""
        raise RuntimeError(f"ffmpeg failed ({res.returncode}): {' '.join(args[:6])} ...\n{err}")
    return res


def exact_duration(path: Path) -> float:
    """Duration in seconds, measured by decoding to 48 kHz mono PCM and counting samples."""
    res = run_ffmpeg(["-i", str(path), "-vn", "-ac", "1", "-ar", str(SAMPLE_RATE), "-f", "s16le", "-"])
    return len(res.stdout) / 2 / SAMPLE_RATE


def detect_silences(path: Path, noise_db: float = -38.0, min_dur: float = 0.16) -> list[tuple[float, float]]:
    """(start, end) of pauses in speech, for snapping caption/reveal timing to real sentence gaps."""
    res = run_ffmpeg(["-i", str(path), "-af", f"silencedetect=noise={noise_db}dB:d={min_dur}", "-f", "null", "-"])
    text = res.stderr.decode("utf-8", "replace")
    starts = [float(x) for x in re.findall(r"silence_start: (-?[\d.]+)", text)]
    ends = [float(x) for x in re.findall(r"silence_end: ([\d.]+)", text)]
    out = []
    for i, s in enumerate(starts):
        e = ends[i] if i < len(ends) else None
        if e is not None:
            out.append((max(0.0, s), e))
    return out


def measure_loudness(path: Path) -> dict:
    """Integrated loudness (LUFS), true peak and LRA via loudnorm's analysis pass."""
    res = run_ffmpeg(["-i", str(path), "-vn", "-af", "loudnorm=I=-16:TP=-1.5:LRA=11:print_format=json", "-f", "null", "-"])
    text = res.stderr.decode("utf-8", "replace")
    m = re.search(r"\{[^{}]*\"input_i\"[^{}]*\}", text, re.S)
    if not m:
        raise RuntimeError("could not parse loudnorm output")
    return json.loads(m.group(0))
