"""YouTube metadata (title, description with chapters, tags) for one lecture."""
from __future__ import annotations

import re

from .config import Config

TITLE_MAX = 100
DESC_MAX = 5000
TAGS_MAX_CHARS = 480  # YouTube allows 500 incl. separators; keep margin


def fmt_chapter(seconds: float) -> str:
    s = int(seconds)
    h, rem = divmod(s, 3600)
    m, sec = divmod(rem, 60)
    return f"{h}:{m:02d}:{sec:02d}" if h else f"{m}:{sec:02d}"


def chapters(timeline: dict, min_len: float = 10.0) -> list[tuple[float, str]]:
    """YouTube chapters: first at 0:00, each ≥ 10 s, consecutive duplicates merged."""
    items: list[tuple[float, str]] = []
    for i, sc in enumerate(timeline["scenes"]):
        start = 0.0 if i == 0 else sc["start"]
        title = sc["chapter"]
        if items and items[-1][1] == title:
            continue
        items.append((start, title))
    end = timeline["outro"]["start"]
    merged: list[tuple[float, str]] = []
    for j, (start, title) in enumerate(items):
        nxt = items[j + 1][0] if j + 1 < len(items) else end
        if merged and nxt - start < min_len:
            continue  # too short: fold into the previous chapter
        merged.append((start, title))
    return merged


def youtube_title(plan: dict) -> str:
    lesson = plan["lectureTitle"]
    course = plan["course"]["title"]
    full = f"{lesson} | {course}"
    if len(full) <= TITLE_MAX:
        return full
    short_course = re.split(r"[:(]", course)[0].strip()
    full = f"{lesson} | {short_course}"
    if len(full) <= TITLE_MAX:
        return full
    return lesson if len(lesson) <= TITLE_MAX else lesson[: TITLE_MAX - 1].rstrip() + "…"


def tags_for(plan: dict, pack: dict) -> list[str]:
    raw = ["Optimize All Academy", plan["course"]["title"], plan["lectureTitle"]]
    raw += list(pack.get("tools") or [])
    raw += list(pack.get("skills") or [])
    cat = (plan["course"].get("category") or "").strip()
    raw += [cat.upper() if len(cat) <= 3 else cat.title(), "online course", "tutorial"]
    out, total, seen = [], 0, set()
    for t in raw:
        t = re.sub(r"[<>\"]", "", str(t)).strip()[:60]
        if not t or t.lower() in seen:
            continue
        cost = len(t) + (2 if " " in t else 0) + 1  # quotes around multi-word tags + comma
        if total + cost > TAGS_MAX_CHARS:
            break
        out.append(t)
        seen.add(t.lower())
        total += cost
    return out


def build(cfg: Config, plan: dict, timeline: dict, pack: dict, *, credits: float | None = None) -> dict:
    first = plan["scenes"][0]["narration"]
    sentences = re.split(r"(?<=[.!?])\s+", first)
    hook = " ".join(sentences[:3])
    ch_lines = [f"{fmt_chapter(t)} {title}" for t, title in chapters(timeline)]
    lesson_url = plan["lessonUrl"]
    course_url = lesson_url.rsplit("/", 1)[0]
    desc = "\n".join(
        [
            hook,
            "",
            f"Full lesson, code, knowledge check and certificate: {lesson_url}",
            f"Course: {plan['course']['title']} — {course_url}",
            "",
            "Chapters",
            *ch_lines,
            "",
            f"Module {plan['module']['index']}: {plan['module']['title']} · Lesson {plan['lesson']['number']}",
            "Narration uses an AI voice (ElevenLabs); script written and reviewed by Optimize All Academy.",
            "",
            "#OptimizeAll #OnlineLearning",
        ]
    )
    if len(desc) > DESC_MAX:
        desc = desc[: DESC_MAX - 1]
    return {
        "key": plan["key"],
        "snippet": {
            "title": youtube_title(plan),
            "description": desc,
            "tags": tags_for(plan, pack),
            "categoryId": cfg.youtube_category_id,
            "defaultLanguage": cfg.youtube_language,
            "defaultAudioLanguage": cfg.youtube_language,
        },
        "status": {
            "privacyStatus": cfg.youtube_privacy,
            "selfDeclaredMadeForKids": False,
            "embeddable": True,
            "license": "youtube",
            "containsSyntheticMedia": True,
        },
        "playlist": {"title": plan["course"]["title"], "description": f"Video lectures for the Optimize All Academy course {plan['course']['title']}. {course_url}"},
        "captions": {"language": cfg.youtube_language, "name": "English"},
        "lessonUrl": lesson_url,
        "durationSeconds": timeline["totalSeconds"],
        "chapters": ch_lines,
        "narrationCredits": credits,
    }
