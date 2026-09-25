"""`plan`: pack + lesson -> a deterministic render plan (template per scene, chapters, TTS text, cache keys)."""
from __future__ import annotations

import hashlib
import re

from .config import VOICE_NAMES, Config
from .packs import (
    LectureRef,
    apply_pronunciations,
    extract_code_blocks,
    split_sentences,
    tokens,
)

PLAN_VERSION = 1

TEMPLATES = ("title", "bullets", "flow", "code", "compare", "chart", "keyidea", "case", "recap")

CODE_WORDS = re.compile(
    r"\b(code|editor|terminal|script|snippet|commands?|cli|git|runner|console|notebook|config|yaml|json|sql|"
    r"python|function|query|prompt card|output)\b",
    re.I,
)
DOC_WORDS = re.compile(r"\b(document|file|template|checklist|prompt)\b", re.I)
FLOW_WORDS = re.compile(r"\b(loop|flow|pipeline|cycle|circular|funnel|feeding into|feeds? into|stages|sequence)\b", re.I)
CONVERGE_WORDS = re.compile(r"\b(feeding into|feed into|merge[ds]?|into one|converg\w*|combine[ds]?)\b", re.I)
LOOP_WORDS = re.compile(r"\b(loop|cycle|circular|repeat)\b", re.I)
CHART_WORDS = re.compile(r"\b(curves?|chart|graph|line chart|plot|bars?|histogram|axis)\b", re.I)
RECAP_TITLE = re.compile(r"^(recap|summary|wrap[- ]?up|key takeaways|in summary)\b", re.I)
KEYIDEA_TITLE = re.compile(r"^(the )?(principle|golden rule|key idea|big idea|core idea|the rule)\b|\bkey idea\b", re.I)
CASE_TITLE = re.compile(r"^(example|case study|case|worked example|scenario|story)\b|\(illustrative\)", re.I)
PITFALL_TITLE = re.compile(r"\b(mistakes?|pitfalls?|avoid|anti-patterns?|red flags?|don'?t)\b", re.I)
TRY_ITEM = re.compile(r"^(try( this)?( now)?|do this now|your turn)\s*:\s*", re.I)
NUMBER_WORDS ={"two": 2, "three": 3, "four": 4, "2": 2, "3": 3, "4": 4}


def sha256(*parts: str) -> str:
    h = hashlib.sha256()
    for p in parts:
        h.update(p.encode("utf-8"))
        h.update(b"\x00")
    return h.hexdigest()


def tts_cache_key(voice_id: str, model_id: str, text: str) -> str:
    """Cache key for a narration clip: sha256(voice + model + exact text sent to TTS)."""
    return sha256(voice_id, model_id, text)


def parse_on_screen(on_screen: str) -> dict:
    lines = [ln.strip() for ln in (on_screen or "").split("\n") if ln.strip()]
    title = lines[0] if lines else ""
    bullets, extras, nxt = [], [], None
    for ln in lines[1:]:
        if ln.startswith("•"):
            bullets.append(ln.lstrip("•").strip())
        elif re.match(r"^next( step)?\s*:", ln, re.I):
            nxt = re.sub(r"^next( step)?\s*:\s*", "", ln, flags=re.I)
        else:
            extras.append(ln)
    return {"title": title, "bullets": bullets, "extras": extras, "next": nxt}


def arrow_chain(text: str) -> list[str]:
    parts = [p.strip() for p in re.split(r"\s*(?:→|->|⟶)\s*", text) if p.strip()]
    return parts if len(parts) >= 3 else []


def chapter_title(parsed: dict, index: int, count: int) -> str:
    title = parsed["title"]
    if index == 0:
        return "Introduction"
    t = re.sub(r"^\s*(step\s*)?\d+[.)]\s*", "", title, flags=re.I)  # "3. Implement" -> "Implement"
    t = re.sub(r"\s*\((illustrative|example)\)\s*$", "", t, flags=re.I)
    t = t.strip(" :-–—")
    if index == count - 1 and RECAP_TITLE.match(t) and "next" not in t.lower():
        t = f"{t} & next step"
    return t[:1].upper() + t[1:] if t else f"Part {index + 1}"


def _label_pairs(bullets: list[str]) -> list[tuple[str, str]]:
    pairs = []
    for b in bullets:
        m = re.match(r"^([^:]{2,28}):\s+(.+)$", b)
        if m:
            pairs.append((m.group(1).strip(), m.group(2).strip()))
    return pairs


def _score_block(block: dict, text_tokens: set[str]) -> int:
    return len(tokens(block["code"] + " " + block["heading"]) & text_tokens)


def choose_template(scene: dict, parsed: dict, index: int, count: int, blocks: list[dict], used: set[int]) -> tuple[str, dict]:
    """Deterministic template choice. Returns (template, template-specific data)."""
    visual = scene.get("visual", "")
    title = parsed["title"]
    bullets = parsed["bullets"]
    all_text = " ".join([scene.get("narration", ""), scene.get("onScreen", ""), visual])

    if index == 0:
        return "title", {"chain": arrow_chain(title)}

    if index == count - 1 or RECAP_TITLE.match(title):
        nxt = parsed["next"]
        items = list(bullets)
        if not nxt and "next" in title.lower() and items:
            nxt = items.pop()
        return "recap", {"items": items, "next": nxt}

    # Code on screen: the visual asks for code/terminal/etc. and a lesson code block matches the scene.
    code_ask = bool(CODE_WORDS.search(visual))
    doc_ask = bool(DOC_WORDS.search(visual))
    if (code_ask or doc_ask) and blocks:
        tt = tokens(all_text)
        scored = sorted(
            ((_score_block(b, tt), -b["index"], b) for b in blocks if b["index"] not in used),
            key=lambda x: (x[0], x[1]),
            reverse=True,
        )
        if scored:
            score, _, best = scored[0]
            need = 2 if code_ask else 5
            if score >= need:
                used.add(best["index"])
                return "code", {"block": best}

    if KEYIDEA_TITLE.search(title) and bullets:
        return "keyidea", {"statement": bullets[0], "supporting": bullets[1:]}

    if CASE_TITLE.search(title) or any(re.match(r"^(case|example)\s*:", b, re.I) for b in bullets):
        return "case", {}

    if PITFALL_TITLE.search(title) and bullets:
        tries = [b for b in bullets if TRY_ITEM.match(b)]
        rest = [b for b in bullets if not TRY_ITEM.match(b)]
        data = {"layout": "list", "variant": "pitfalls", "items": rest}
        if tries:
            data["tryItem"] = TRY_ITEM.sub("", tries[0]).strip()
        return "bullets", data

    chain = arrow_chain(title) or next((arrow_chain(b) for b in bullets if arrow_chain(b)), [])
    short = bullets and all(len(b) <= 40 for b in bullets) and 3 <= len(bullets) <= 5
    if chain or (FLOW_WORDS.search(visual) and short):
        nodes = chain or bullets
        if CONVERGE_WORDS.search(visual) and not chain and len(nodes) >= 3:
            shape = "converge"
        elif LOOP_WORDS.search(visual) or LOOP_WORDS.search(title):
            shape = "loop"
        else:
            shape = "chain"
        return "flow", {"nodes": nodes, "shape": shape}

    if CHART_WORDS.search(visual) and re.search(r"\bcurves?\b", visual, re.I):
        m = re.search(r"\b(two|three|four|[234])\s+curves\b", visual, re.I)
        curves = NUMBER_WORDS.get(m.group(1).lower(), 1) if m else 1
        axes = ["Spend", "Response"] if re.search(r"\b(spend|budget|roas)\b", all_text, re.I) else ["Input", "Outcome"]
        return "chart", {
            "kind": "curve",
            "curves": curves,
            "tangent": bool(re.search(r"\b(tangent|slope|marginal)\b", all_text, re.I)),
            "axes": axes,
            "annotate": "hill" if re.search(r"half-saturation|slider|knob|hill", all_text, re.I) else None,
        }

    pairs = _label_pairs(bullets)
    if re.search(r"\b(vs\.?|versus)\b", title, re.I) or len(pairs) >= 2:
        if len(pairs) >= 2:
            left, right = pairs[0], pairs[1]
            rest = [b for b in bullets if not any(b.startswith(p[0] + ":") for p in (left, right))]
        else:
            m = re.split(r"\s+(?:vs\.?|versus)\s+", title, maxsplit=1, flags=re.I)
            half = (len(bullets) + 1) // 2
            left = (m[0].strip(), " · ".join(bullets[:half]))
            right = (m[1].strip() if len(m) > 1 else "", " · ".join(bullets[half:]))
            rest = []
        return "compare", {
            "left": {"label": left[0], "text": left[1]},
            "right": {"label": right[0], "text": right[1]},
            "notes": rest,
        }

    # Alternate layouts deterministically so consecutive bullet scenes do not look alike.
    n = len(bullets)
    if n == 2:
        layout = "cards"
    elif n == 3:
        layout = ("cards", "list")[index % 2]
    elif n == 4:
        layout = ("grid", "list")[index % 2]
    else:
        layout = "list"
    return "bullets", {"layout": layout}


def build_plan(ref: LectureRef, cfg: Config) -> dict:
    pack, lesson, lecture = ref.pack, ref.lesson, ref.lecture
    voice = cfg.voice_for(pack.get("category"))
    pron = lecture.get("pronunciations") or []
    blocks = extract_code_blocks(lesson.get("body", ""))
    scenes_in = lecture["scenes"]
    count = len(scenes_in)
    used: set[int] = set()
    scenes = []
    for i, s in enumerate(scenes_in):
        parsed = parse_on_screen(s.get("onScreen", ""))
        template, data = choose_template(s, parsed, i, count, blocks, used)
        tts = apply_pronunciations(" ".join(s["narration"].split()), pron)
        scenes.append(
            {
                "index": i,
                "id": f"s{i + 1:02d}",
                "template": template,
                "data": data,
                "chapter": chapter_title(parsed, i, count),
                "title": parsed["title"],
                "bullets": parsed["bullets"],
                "extras": parsed["extras"],
                "next": parsed["next"],
                "visual": s.get("visual", ""),
                "narration": s["narration"],
                "tts": tts,
                "sentences": split_sentences(s["narration"]),
                "ttsKey": tts_cache_key(voice, cfg.model_id, tts),
                "targetSeconds": s.get("seconds"),
            }
        )
    lesson_number = ref.lesson_index + 1
    return {
        "planVersion": PLAN_VERSION,
        "key": ref.key,
        "course": {
            "slug": pack["slug"],
            "title": pack.get("title", pack["slug"]),
            "category": pack.get("category"),
            "level": pack.get("level"),
            "lastReviewed": pack.get("lastReviewed"),
        },
        "module": {"index": ref.module_index + 1, "title": ref.module.get("title", "")},
        "lesson": {"slug": ref.lesson_slug, "title": lesson.get("title", ref.lesson_slug), "number": lesson_number},
        "lectureTitle": ref.title,
        "voice": {"id": voice, "name": VOICE_NAMES.get(voice, voice)},
        "model": cfg.model_id,
        "lessonUrl": cfg.lesson_url(pack["slug"], ref.lesson_slug),
        "ttsCharacters": sum(len(sc["tts"]) for sc in scenes),
        "scenes": scenes,
    }
