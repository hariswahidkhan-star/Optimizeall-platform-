"""WebVTT captions from the timeline (sentence timing snapped to real pauses, chunked for readability)."""
from __future__ import annotations


def ts(seconds: float) -> str:
    ms = int(round(max(0.0, seconds) * 1000))
    h, ms = divmod(ms, 3_600_000)
    m, ms = divmod(ms, 60_000)
    s, ms = divmod(ms, 1000)
    return f"{h:02d}:{m:02d}:{s:02d}.{ms:03d}"


def wrap(text: str, width: int = 42) -> str:
    """Wrap to at most two balanced lines (YouTube/Coursera caption style)."""
    if len(text) <= width:
        return text
    words = text.split()
    best, best_diff = None, None
    for i in range(1, len(words)):
        a, b = " ".join(words[:i]), " ".join(words[i:])
        diff = abs(len(a) - len(b))
        if max(len(a), len(b)) <= width + 12 and (best_diff is None or diff < best_diff):
            best, best_diff = (a, b), diff
    if best is None:
        mid = len(words) // 2
        best = (" ".join(words[:mid]), " ".join(words[mid:]))
    return "\n".join(best)


def cues_from_timeline(timeline: dict) -> list[dict]:
    cues = []
    for sc in timeline["scenes"]:
        for c in sc["cues"]:
            cues.append({"start": sc["start"] + c["start"], "end": sc["start"] + c["end"], "text": c["text"]})
    # a cue shorter than ~0.7 s is unreadable: borrow time from the gap after it when possible
    for i, c in enumerate(cues):
        nxt = cues[i + 1]["start"] if i + 1 < len(cues) else c["end"] + 2
        if c["end"] - c["start"] < 0.7:
            c["end"] = min(nxt, c["start"] + 0.7)
    return cues


def to_vtt(cues: list[dict]) -> str:
    out = ["WEBVTT", ""]
    for i, c in enumerate(cues, 1):
        out.append(str(i))
        out.append(f"{ts(c['start'])} --> {ts(c['end'])}")
        out.append(wrap(c["text"]))
        out.append("")
    return "\n".join(out)
