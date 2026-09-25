"""Timing: sentence boundaries inside each narration clip, reveal times for on-screen items, caption cues.

Sentence timing is estimated from character counts (speech rate is close to constant for one voice) and then
snapped to real pauses found with ffmpeg's silencedetect, so captions and reveals land on actual sentence gaps.
If the TTS backend returned character alignment (REST backend), that is used instead and is exact.
"""
from __future__ import annotations

import math
import re

from .packs import split_caption_chunks, tokens

SNAP_WINDOW = 1.3  # seconds a boundary may move to reach a real pause


def _stem(w: str) -> str:
    for suf in ("ing", "ed", "es", "s"):
        if len(w) > 4 and w.endswith(suf):
            return w[: -len(suf)]
    return w


def stems(text: str) -> set[str]:
    return {_stem(t) for t in tokens(text)}


def speech_bounds(duration: float, silences: list[tuple[float, float]]) -> tuple[float, float]:
    """Where speech actually starts/ends inside the clip (skip leading/trailing silence)."""
    start, end = 0.0, duration
    for s, e in silences:
        if s <= 0.05 and e < min(1.5, duration / 3):
            start = e
        if e >= duration - 0.05 and s > duration * 0.6:
            end = s
    return start, max(end, start + 0.1)


def sentence_times(sentences: list[str], duration: float, silences: list[tuple[float, float]] | None = None,
                   alignment: dict | None = None) -> list[tuple[float, float]]:
    """(start, end) of each sentence within the clip, in seconds."""
    if not sentences:
        return []
    if alignment and alignment.get("characters") and alignment.get("character_start_times_seconds"):
        return _from_alignment(sentences, alignment, duration)
    silences = silences or []
    s0, s1 = speech_bounds(duration, silences)
    span = s1 - s0
    weights = [len(s) + 12 for s in sentences]  # +12 chars ~ the pause after each sentence
    total = sum(weights)
    bounds = [s0]
    acc = 0.0
    for w in weights[:-1]:
        acc += w
        bounds.append(s0 + span * acc / total)
    bounds.append(s1)
    # Snap each interior boundary to the best nearby pause (longer pauses preferred), keeping order.
    pauses = [((a + b) / 2, b - a) for a, b in silences if s0 + 0.2 < (a + b) / 2 < s1 - 0.2]
    prev = s0
    for i in range(1, len(bounds) - 1):
        target = bounds[i]
        best, best_score = None, None
        for mid, length in pauses:
            if mid <= prev + 0.3 or abs(mid - target) > SNAP_WINDOW:
                continue
            score = abs(mid - target) - 0.9 * min(length, 0.8)
            if best_score is None or score < best_score:
                best, best_score = mid, score
        if best is not None:
            bounds[i] = best
        bounds[i] = max(bounds[i], prev + 0.3)
        prev = bounds[i]
    return [(round(bounds[i], 3), round(bounds[i + 1], 3)) for i in range(len(sentences))]


def _from_alignment(sentences, alignment, duration):
    chars = alignment["characters"]
    starts = alignment["character_start_times_seconds"]
    ends = alignment.get("character_end_times_seconds") or starts
    text = "".join(chars)
    out, pos = [], 0
    for s in sentences:
        # alignment text is the TTS text (pronunciations applied), so locate sentences by first/last word
        first = re.escape(s.split()[0])
        m = re.search(first, text[pos:])
        a = pos + (m.start() if m else 0)
        b = min(len(text), a + len(s))
        out.append((float(starts[min(a, len(starts) - 1)]), float(ends[min(b - 1, len(ends) - 1)])))
        pos = b
    fixed = []
    for i, (a, b) in enumerate(out):
        nxt = out[i + 1][0] if i + 1 < len(out) else duration
        fixed.append((round(a, 3), round(max(b, min(nxt, a + 0.3)), 3)))
    return fixed


def match_items(items: list[str], sentences: list[str], times: list[tuple[float, float]]) -> list[float | None]:
    """Time (relative to clip) at which each on-screen item is first spoken. Monotonic; None if unmatched."""
    out: list[float | None] = []
    last_j, last_t = 0, -1.0
    sent_stems = [stems(s) for s in sentences]
    for item in items:
        it = stems(item)
        best_j, best_score = None, 0
        for j in range(last_j, len(sentences)):
            score = len(it & sent_stems[j])
            if score > best_score:
                best_j, best_score = j, score
        if best_j is None:
            out.append(None)
            continue
        # position inside the sentence: first matching word
        sent = sentences[best_j]
        words = re.findall(r"[A-Za-z0-9][\w'-]*", sent)
        offset = 0
        cursor = 0
        for w in words:
            idx = sent.find(w, cursor)
            cursor = idx + len(w)
            if _stem(w.lower().strip(".-")) in it:
                offset = idx
                break
        a, b = times[best_j]
        t = a + (b - a) * (offset / max(1, len(sent))) * 0.9
        if t <= last_t:
            t = last_t + 0.5
        out.append(round(t, 3))
        last_j, last_t = best_j, t
    return out


def fill_reveals(raw: list[float | None], lo: float, hi: float, min_gap: float = 0.55) -> list[float]:
    """Interpolate unmatched reveals and enforce ordering/min gaps within [lo, hi]."""
    n = len(raw)
    if n == 0:
        return []
    vals = list(raw)
    known = [i for i, v in enumerate(vals) if v is not None]
    if not known:
        return [round(lo + (hi - lo) * i / max(1, n - 1) if n > 1 else lo, 3) for i in range(n)]
    for i in range(n):
        if vals[i] is not None:
            continue
        prev = max((k for k in known if k < i), default=None)
        nxt = min((k for k in known if k > i), default=None)
        if prev is None:
            a, ia = lo, -1
        else:
            a, ia = vals[prev], prev
        if nxt is None:
            b, ib = hi, n
        else:
            b, ib = vals[nxt], nxt
        vals[i] = a + (b - a) * (i - ia) / (ib - ia)
    res = []
    prev = lo - min_gap
    for v in vals:
        v = max(v, prev + min_gap, lo)
        res.append(round(min(v, hi + 3.0), 3))
        prev = v
    return res


def caption_cues(sentences: list[str], times: list[tuple[float, float]], offset: float) -> list[dict]:
    """Caption cues (absolute to `offset`), chunked to ≤ 84 chars and timed by characters within a sentence."""
    cues = []
    for sent, (a, b) in zip(sentences, times):
        chunks = split_caption_chunks(sent)
        total = sum(len(c) for c in chunks)
        t = a
        for c in chunks:
            d = (b - a) * len(c) / max(1, total)
            cues.append({"start": round(offset + t, 3), "end": round(offset + t + d, 3), "text": c})
            t += d
    # never overlap, and keep a minimum on-screen time
    for i in range(len(cues) - 1):
        cues[i]["end"] = min(cues[i]["end"], cues[i + 1]["start"])
    return cues


def frames_for(seconds: float, fps: int) -> int:
    return int(math.ceil(seconds * fps - 1e-6))
