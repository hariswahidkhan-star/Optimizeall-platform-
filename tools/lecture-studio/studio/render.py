"""`render`: plan + narration -> timeline.json -> per-scene H.264 segments via the Playwright renderer."""
from __future__ import annotations

import hashlib
import json
import math
import re
import subprocess
import time
from pathlib import Path

from . import timeline as tl
from .audio import detect_silences, ffmpeg_exe
from .config import TOOL_DIR, Config
from .packs import find_lecture, load_pack, split_sentences
from .workspace import LectureDir, read_json, write_json

RENDERER = TOOL_DIR / "renderer"
FILENAMES = {"python": "optimizer.py", "bash": "terminal", "text": "prompt.txt", "md": "PLAN.md"}


def _font(cfg: Config, pkg: str, file: str) -> str | None:
    for base in (TOOL_DIR / "node_modules", cfg.node_modules):
        p = Path(base) / "@fontsource-variable" / pkg / "files" / file
        if p.exists():
            return str(p)
    return None


def fonts(cfg: Config) -> dict:
    return {
        "inter": _font(cfg, "inter", "inter-latin-wght-normal.woff2"),
        "interTight": _font(cfg, "inter-tight", "inter-tight-latin-wght-normal.woff2"),
        "mono": _font(cfg, "jetbrains-mono", "jetbrains-mono-latin-wght-normal.woff2"),
    }


def _code_filename(block: dict) -> str:
    lang = block["lang"]
    code = block["code"]
    if lang in ("md", "markdown"):
        m = re.search(r"\b([A-Z][A-Z_]+\.md)\b", code)
        return m.group(1) if m else ("PLAN.md" if code.lower().startswith("plan") else "notes.md")
    if lang in ("bash", "sh", "shell", "zsh", "console"):
        return "terminal"
    if lang in ("py", "python"):
        m = re.search(r"\b([a-z_]+\.py)\b", code)
        return m.group(1) if m else "main.py"
    if lang in ("text", "txt", "prompt", ""):
        return "prompt.txt"
    ext = {"typescript": "ts", "javascript": "js", "yaml": "yml"}.get(lang, lang)
    return f"snippet.{ext}"


def _code_highlights(block: dict, sentences: list[str], times: list[tuple[float, float]], start: float,
                     audio_start: float, audio_dur: float, type_end: float) -> list[dict]:
    """Chunk the code (by blank lines / size) and time a highlight walk-through across the narration."""
    lines = block["code"].split("\n")
    groups, cur = [], []
    for i, ln in enumerate(lines):
        if ln.strip() == "" and cur:
            groups.append(cur)
            cur = []
        elif ln.strip():
            cur.append(i)
    if cur:
        groups.append(cur)
    # merge tiny groups / split giant ones so we end with 2..4 chunks
    chunks = []
    for g in groups:
        if len(g) > 8:
            size = math.ceil(len(g) / math.ceil(len(g) / 6))
            chunks += [g[i:i + size] for i in range(0, len(g), size)]
        else:
            chunks.append(g)
    while len(chunks) > 4:
        k = min(range(len(chunks) - 1), key=lambda i: len(chunks[i]) + len(chunks[i + 1]))
        chunks[k:k + 2] = [chunks[k] + chunks[k + 1]]
    if len(chunks) == 1 and len(chunks[0]) >= 4:
        g = chunks[0]
        half = len(g) // 2
        chunks = [g[:half], g[half:]]
    if len(chunks) <= 1:
        return []
    lo = max(type_end + 0.4, audio_start + audio_dur * 0.3)
    hi = audio_start + audio_dur * 0.86
    raw = tl.match_items([" ".join(lines[i] for i in ch) for ch in chunks], sentences, times)
    raw = [None if r is None else audio_start + r for r in raw]
    at = tl.fill_reveals(raw, lo, hi, min_gap=2.0)
    return [{"from": ch[0], "to": ch[-1], "at": t} for ch, t in zip(chunks, at)]


def build_timeline(cfg: Config, plan: dict, narration: dict, *, burn_captions: bool = False) -> dict:
    fps = cfg.fps
    audio = {s["id"]: s for s in narration["scenes"]}
    ref = find_lecture(load_pack(cfg.catalog_dir, plan["course"]["slug"]), plan["lesson"]["slug"])
    pack = ref.pack
    # next lesson (for the outro card)
    all_lessons = [l for m in pack["modules"] for l in m["lessons"]]
    idx = next(i for i, l in enumerate(all_lessons) if l["slug"] == plan["lesson"]["slug"])
    next_lesson = all_lessons[idx + 1] if idx + 1 < len(all_lessons) else None

    scenes_out = []
    t_cursor = cfg.intro_seconds
    count = len(plan["scenes"])
    for sc in plan["scenes"]:
        a = audio[sc["id"]]
        dur = float(a["seconds"])
        silences = detect_silences(Path(a["file"])) if a.get("file") else []
        sentences = sc["sentences"]
        tts_sents = split_sentences(sc["tts"])
        weights_src = tts_sents if len(tts_sents) == len(sentences) else sentences
        stimes = tl.sentence_times(weights_src, dur, silences, a.get("alignment"))
        lead = cfg.lead_in
        frames = tl.frames_for(lead + dur + cfg.tail, fps)
        seconds = frames / fps
        data = dict(sc["data"])
        tpl = sc["template"]
        # which on-screen items reveal on narration?
        if tpl == "compare":
            items = [f"{data['left']['label']} {data['left']['text']}", f"{data['right']['label']} {data['right']['text']}"] + data["notes"]
        elif tpl == "flow":
            items = data["nodes"]
        elif tpl == "keyidea":
            items = data["supporting"]
        elif tpl == "recap":
            items = data["items"] + ([data["next"]] if data.get("next") else [])
        else:
            items = list(data.get("items") or sc["bullets"])
            if tpl == "bullets" and data.get("tryItem"):
                items.append(data["tryItem"])
        raw = tl.match_items(items, sentences, stimes)
        raw = [None if r is None else lead + r for r in raw]
        lo = lead + 0.5 if tpl != "title" else 1.4
        hi = lead + dur * 0.85
        reveals = tl.fill_reveals(raw, lo, hi)
        if tpl == "recap" and data.get("next"):
            nxt_i = next((i for i, s in enumerate(sentences) if re.search(r"\bnext\b", s, re.I)), None)
            data["nextAt"] = round(lead + stimes[nxt_i][0], 3) if nxt_i is not None else reveals[-1]
        if tpl == "bullets" and data.get("tryItem"):
            try_i = next((i for i, s in enumerate(sentences) if re.search(r"\btry this\b|\btry\b", s, re.I)), None)
            data["tryAt"] = round(lead + stimes[try_i][0], 3) if try_i is not None else reveals[-1]
        if tpl == "chart":
            ci = next((i for i, s in enumerate(sentences) if re.search(r"\bcurves?\b|\bshape\b", s, re.I)), 0)
            data["curveAt"] = round(max(lead + 0.6, lead + stimes[ci][0]), 3)
            ti = next((i for i, s in enumerate(sentences) if i > ci and re.search(r"\b(slope|marginal|tangent|equal)\b", s, re.I)), None)
            data["tangentAt"] = round(lead + stimes[ti][0], 3) if ti is not None else round(lead + dur * 0.55, 3)
        if tpl == "code":
            block = data.pop("block")
            n_lines = len(block["code"].split("\n"))
            type_end = lead + 0.6 + min(dur * 0.35, 0.18 * n_lines + 0.6)
            data["code"] = {"lang": block["lang"], "code": block["code"], "filename": _code_filename(block)}
            data["highlights"] = _code_highlights(block, sentences, stimes, 0, lead, dur, type_end)
        cues_rel = tl.caption_cues(sentences, stimes, lead)
        scenes_out.append(
            {
                "kind": "scene",
                "id": sc["id"],
                "index": sc["index"],
                "template": tpl,
                "data": data,
                "title": sc["title"],
                "bullets": sc["bullets"],
                "extras": sc["extras"],
                "narration": sc["narration"],
                "chapter": sc["chapter"],
                "chapterIndex": sc["index"] + 1,
                "chapterCount": count,
                "course": plan["course"],
                "lesson": plan["lesson"],
                "module": plan["module"],
                "lectureTitle": plan["lectureTitle"],
                "duration": seconds,
                "frames": frames,
                "audioStart": lead,
                "audioDuration": dur,
                "audioFile": a["file"],
                "sentences": [{"text": s, "start": round(lead + x, 3), "end": round(lead + y, 3)} for s, (x, y) in zip(sentences, stimes)],
                "reveals": reveals,
                "cues": cues_rel,
                "captions": cues_rel if burn_captions else [],
                "start": round(t_cursor, 3),
            }
        )
        t_cursor += seconds
    host = re.sub(r"^https?://(www\.)?", "", cfg.site_base_url).rstrip("/")
    common = {"course": plan["course"], "lesson": plan["lesson"], "module": plan["module"], "lectureTitle": plan["lectureTitle"]}
    intro_frames = tl.frames_for(cfg.intro_seconds, fps)
    outro_frames = tl.frames_for(cfg.outro_seconds, fps)
    intro = {"kind": "intro", "id": "intro", "duration": intro_frames / fps, "frames": intro_frames, **common}
    outro = {
        "kind": "outro", "id": "outro", "duration": outro_frames / fps, "frames": outro_frames, **common,
        "siteHost": host, "lessonUrl": plan["lessonUrl"].replace("https://", ""),
        "nextLesson": {"slug": next_lesson["slug"], "title": next_lesson.get("title", "")} if next_lesson else None,
        "start": round(t_cursor, 3),
    }
    total = cfg.intro_seconds + sum(s["duration"] for s in scenes_out) + outro["duration"]
    return {"key": plan["key"], "fps": fps, "intro": intro, "scenes": scenes_out, "outro": outro,
            "totalSeconds": round(total, 3), "burnCaptions": burn_captions}


def preview_narration(plan: dict, words_per_second: float = 2.4) -> dict:
    """Estimated narration timing for free design previews before any TTS spend."""
    return {"key": plan["key"], "credits": 0, "scenes": [
        {"id": s["id"], "file": None, "seconds": round(max(6.0, len(s["narration"].split()) / words_per_second), 2)}
        for s in plan["scenes"]]}


def still_time(sc: dict) -> float:
    """A representative review frame: everything revealed, before the exit animation."""
    d = sc["duration"]
    if sc["kind"] != "scene":
        return round(min(d - 0.8, 2.4), 3)
    data = sc.get("data", {})
    events = list(sc.get("reveals") or []) + [data.get(k) for k in ("nextAt", "tryAt", "tangentAt", "curveAt") if data.get(k)]
    events += [h["at"] for h in data.get("highlights", [])]
    last = max(events) if events else d * 0.6
    return round(min(d - 0.6, max(3.2, last + 2.0)), 3)


def _stage_hash() -> str:
    h = hashlib.sha256()
    for name in ("stage.html", "stage.css", "stage.js", "render.mjs"):
        h.update((RENDERER / name).read_bytes())
    return h.hexdigest()[:16]


def _item_hash(scene: dict, cfg: Config) -> str:
    payload = {k: v for k, v in scene.items() if k not in ("start", "audioFile")}
    blob = json.dumps(payload, sort_keys=True).encode() + _stage_hash().encode() + f"{cfg.width}x{cfg.height}@{cfg.fps}".encode()
    return hashlib.sha256(blob).hexdigest()[:20]


def render_lecture(cfg: Config, plan: dict, ldir: LectureDir, *, workers: int | None = None, only: str | None = None,
                   stills: bool = False, force: bool = False, burn_captions: bool = False, stills_only: bool = False,
                   preview: bool = False) -> dict:
    """Render segments. ``preview=True`` renders review stills from estimated timings (no audio, no credits)."""
    if preview:
        stills_only = True
    narration = None if preview else read_json(ldir.narration)
    if preview:
        narration = preview_narration(plan)
    elif not narration or [s["id"] for s in narration["scenes"]] != [s["id"] for s in plan["scenes"]]:
        from .narrate import collect

        narration = collect(cfg, plan, ldir)
    tline = build_timeline(cfg, plan, narration, burn_captions=burn_captions)
    write_json(ldir.root / "timeline-preview.json" if preview else ldir.timeline, tline)
    still_dir = ldir.root / ("preview" if preview else "stills")
    ldir.segments.mkdir(parents=True, exist_ok=True)
    wanted = set(only.split(",")) if only else None
    items = []
    for sc in [tline["intro"], *tline["scenes"], tline["outro"]]:
        if wanted and sc["id"] not in wanted:
            continue
        out = ldir.segments / f"{sc['id']}.mp4"
        meta = ldir.segments / f"{sc['id']}.json"
        hsh = _item_hash(sc, cfg)
        still_list = []
        if stills or stills_only:
            d = sc["duration"]
            at = [still_time(sc)]
            still_list = [{"t": t, "out": str(still_dir / f"{sc['id']}.png")} for t in at]
        if not force and not stills_only and out.exists() and (read_json(meta, {}) or {}).get("hash") == hsh:
            if still_list:
                items.append({"id": sc["id"], "out": str(out), "scene": sc, "frames": sc["frames"], "stills": still_list, "stillsOnlyItem": True})
            continue
        items.append({"id": sc["id"], "out": str(out), "scene": sc, "frames": sc["frames"], "stills": still_list, "hash": hsh})
    if not items:
        print("render: all segments up to date")
        return tline
    job = {
        "width": cfg.width, "height": cfg.height, "fps": cfg.fps, "quality": 94, "crf": 14, "preset": "veryfast",
        "workers": workers or cfg.render_workers, "ffmpeg": ffmpeg_exe(), "fonts": fonts(cfg),
        "nodeModules": str(cfg.node_modules), "stillsOnly": stills_only,
        "items": [i for i in items if not i.get("stillsOnlyItem")],
    }
    still_items = [i for i in items if i.get("stillsOnlyItem")]
    t0 = time.time()
    if job["items"]:
        _run_node(ldir, job, "job")
    if still_items:
        _run_node(ldir, {**job, "stillsOnly": True, "items": still_items}, "stills-job")
    for i in job["items"]:
        if not stills_only:
            write_json(Path(i["out"]).with_suffix(".json"), {"hash": i["hash"], "frames": i["frames"]})
    print(f"render: {len(job['items'])} segment(s) in {time.time() - t0:.1f}s")
    return tline


def _run_node(ldir: LectureDir, job: dict, name: str) -> None:
    job_path = ldir.root / f"render-{name}.json"
    write_json(job_path, job)
    res = subprocess.run(["node", str(RENDERER / "render.mjs"), str(job_path)], cwd=str(TOOL_DIR))
    if res.returncode != 0:
        raise RuntimeError("renderer failed (see [render] log above)")
