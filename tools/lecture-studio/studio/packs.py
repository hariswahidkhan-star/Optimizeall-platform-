"""Reading course packs (v2) and the text utilities the rest of the pipeline shares."""
from __future__ import annotations

import json
import re
from dataclasses import dataclass
from pathlib import Path


@dataclass
class LectureRef:
    course_slug: str
    lesson_slug: str
    pack: dict
    module: dict
    lesson: dict
    module_index: int  # 0-based
    lesson_index: int  # 0-based, across the whole course

    @property
    def key(self) -> str:
        return f"{self.course_slug}/{self.lesson_slug}"

    @property
    def lecture(self) -> dict:
        return self.lesson["lecture"]

    @property
    def title(self) -> str:
        return self.lecture.get("title") or self.lesson.get("title") or self.lesson_slug


def load_pack(catalog_dir: Path, course_slug: str) -> dict:
    path = Path(catalog_dir) / f"{course_slug}.json"
    with path.open(encoding="utf-8") as fh:
        return json.load(fh)


def iter_lectures(pack: dict):
    """Yield LectureRef for every lesson that has a lecture block, in course order."""
    n = 0
    for mi, module in enumerate(pack.get("modules", [])):
        for lesson in module.get("lessons", []):
            if lesson.get("lecture") and lesson["lecture"].get("scenes"):
                yield LectureRef(pack["slug"], lesson["slug"], pack, module, lesson, mi, n)
            n += 1


def find_lecture(pack: dict, lesson_slug: str) -> LectureRef:
    for ref in iter_lectures(pack):
        if ref.lesson_slug == lesson_slug:
            return ref
    raise KeyError(f"lesson {lesson_slug!r} with a lecture not found in {pack.get('slug')}")


def is_v2(pack: dict) -> bool:
    return bool(pack.get("lastReviewed"))


# ---------------------------------------------------------------- pronunciations

def apply_pronunciations(text: str, pronunciations: list[dict] | None) -> str:
    """Replace each term with its `say` text (whole-word, case-sensitive; longest terms first)."""
    if not pronunciations:
        return text
    items = sorted(
        (p for p in pronunciations if p.get("term") and p.get("say")),
        key=lambda p: len(p["term"]),
        reverse=True,
    )
    for p in items:
        term = p["term"]
        # Word boundaries only where the term itself starts/ends with a word character.
        pre = r"(?<![\w])" if re.match(r"\w", term[0]) else ""
        post = r"(?![\w])" if re.match(r"\w", term[-1]) else ""
        text = re.sub(pre + re.escape(term) + post, p["say"], text)
    return text


# ---------------------------------------------------------------- sentences

_ABBREV = {"e.g.", "i.e.", "etc.", "vs.", "mr.", "mrs.", "dr.", "no."}


def split_sentences(text: str) -> list[str]:
    """Split spoken narration into sentences (keeps terminal punctuation)."""
    text = " ".join(text.split())
    if not text:
        return []
    parts = re.split(r"(?<=[.!?])\s+(?=[\"'(\[]?[A-Z0-9])", text)
    out: list[str] = []
    for part in parts:
        if out and out[-1].split()[-1].lower() in _ABBREV:
            out[-1] = f"{out[-1]} {part}"
        else:
            out.append(part)
    return out


def split_caption_chunks(sentence: str, max_chars: int = 84) -> list[str]:
    """Split a sentence into balanced caption chunks (≤ max_chars), preferring cuts after , ; : and dashes."""
    sentence = " ".join(sentence.split())
    if len(sentence) <= max_chars:
        return [sentence]
    k = -(-len(sentence) // max_chars)  # ceil
    chunks: list[str] = []
    rest = sentence
    while k > 1 and len(rest) > max_chars:
        target = len(rest) / k
        best, best_score = None, None
        for m in re.finditer(r" ", rest):
            i = m.start()
            if i > max_chars:
                break
            score = abs(i - target) - (14 if rest[i - 1] in ",;:—-" else 0)
            if best_score is None or score < best_score:
                best, best_score = i, score
        if best is None:
            best = rest.rfind(" ", 0, max_chars) if " " in rest[:max_chars] else max_chars
        chunks.append(rest[:best].strip())
        rest = rest[best:].strip()
        k -= 1
    if rest:
        chunks.append(rest)
    return chunks


# ---------------------------------------------------------------- code blocks

_FENCE = re.compile(r"^```([\w+#.-]*)[^\n]*\n(.*?)^```", re.S | re.M)


def extract_code_blocks(body: str) -> list[dict]:
    """All fenced code blocks in the lesson body, with the nearest preceding ### heading."""
    blocks = []
    for m in _FENCE.finditer(body or ""):
        heading = ""
        for h in re.finditer(r"^###\s+(.+)$", body[: m.start()], re.M):
            heading = h.group(1).strip()
        code = m.group(2).rstrip("\n")
        blocks.append({"lang": (m.group(1) or "text").lower(), "code": code, "heading": heading, "index": len(blocks)})
    return blocks


_WORD = re.compile(r"[A-Za-z][A-Za-z0-9_.-]{2,}")
STOP = set(
    """the and for you your with that this from are was were will have has had not but can all any its into out our
    one two three four five then than them they their there what when where which who why how use used using just
    more most some such only also each every very make made may might must should would could about over after
    before here now new first next last step steps lesson lecture scene show shows showing appears screen""".split()
)


def tokens(text: str) -> set[str]:
    return {w.lower().strip(".-") for w in _WORD.findall(text or "") if w.lower() not in STOP}
