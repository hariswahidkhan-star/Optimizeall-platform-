"""`run`: resumable batch orchestrator (plan -> narrate -> render -> assemble -> upload), disk-aware streaming."""
from __future__ import annotations

import json
import shutil
import time
from pathlib import Path

from . import assemble as assemble_mod
from . import narrate, render
from .config import Config
from .packs import is_v2, iter_lectures, load_pack
from .plan import build_plan
from .workspace import LectureDir, read_json, write_json


def resolve(cfg: Config, specs: list[str], all_v2: bool = False) -> list[str]:
    keys: list[str] = []
    if all_v2:
        for p in sorted(Path(cfg.catalog_dir).glob("*.json")):
            pack = json.loads(p.read_text(encoding="utf-8"))
            if is_v2(pack):
                keys += [r.key for r in iter_lectures(pack)]
    for s in specs:
        if "/" in s:
            keys.append(s)
        else:
            keys += [r.key for r in iter_lectures(load_pack(cfg.catalog_dir, s))]
    seen, out = set(), []
    for k in keys:
        if k not in seen:
            seen.add(k)
            out.append(k)
    return out


def dir_bytes(p: Path) -> int:
    return sum(f.stat().st_size for f in p.rglob("*") if f.is_file()) if p.exists() else 0


def log(cfg: Config, event: dict) -> None:
    event = {"ts": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()), **event}
    cfg.work_dir.mkdir(parents=True, exist_ok=True)
    with (cfg.work_dir / "run.log.jsonl").open("a", encoding="utf-8") as fh:
        fh.write(json.dumps(event, ensure_ascii=False) + "\n")
    print(f"[run] {event.get('key', '')} {event['event']} {event.get('detail', '')}".rstrip())


def run_batch(cfg: Config, args, lectures: list[str]) -> dict:
    keys = resolve(cfg, lectures, getattr(args, "all_v2", False))
    if args.limit:
        keys = keys[: args.limit]
    state_path = cfg.work_dir / "run-state.json"
    state = read_json(state_path, {}) or {}
    summary = {"lectures": len(keys), "done": 0, "skipped": 0, "failed": 0, "needNarration": [], "credits": 0.0,
               "missingCharacters": 0}
    lectures_root = cfg.work_dir / "lectures"
    for key in keys:
        st = state.setdefault(key, {})
        try:
            course, lesson = key.split("/", 1)
            from .packs import find_lecture

            ref = find_lecture(load_pack(cfg.catalog_dir, course), lesson)
            plan = build_plan(ref, cfg)
            ldir = LectureDir(cfg, key)
            write_json(ldir.plan, plan)
            fingerprint = "|".join(sc["ttsKey"] for sc in plan["scenes"]) + f"|{plan['lectureTitle']}"
            if st.get("fingerprint") == fingerprint and st.get("stage") in ("assembled", "uploaded") and not (
                    args.upload and st.get("stage") != "uploaded"):
                summary["skipped"] += 1
                continue
            if st.get("fingerprint") != fingerprint:
                st.clear()
                st["fingerprint"] = fingerprint
            req = narrate.requests_for_agent(cfg, plan)
            if req["missing"]:
                summary["missingCharacters"] += req["missingCharacters"]
                if args.dry_run:
                    log(cfg, {"event": "would-narrate", "key": key, "detail": f"{req['missingCharacters']} chars ≈ credits"})
                    continue
                if args.narrate_backend == "api":
                    narrate.synthesize_api(cfg, plan)
                else:
                    summary["needNarration"].append(key)
                    log(cfg, {"event": "needs-narration", "key": key, "detail": f"{len(req['missing'])} scenes"})
                    continue
            if args.dry_run:
                log(cfg, {"event": "would-render", "key": key})
                continue
            used = dir_bytes(lectures_root)
            if used > cfg.max_output_bytes:
                log(cfg, {"event": "disk-guard", "key": key, "detail": f"{used / 1e9:.2f} GB of outputs; stopping"})
                break
            nar = narrate.collect(cfg, plan, ldir)
            t0 = time.time()
            render.render_lecture(cfg, plan, ldir, workers=args.workers)
            rep = assemble_mod.assemble(cfg, plan, ldir, out_dir=args.out)
            st.update({"stage": "assembled", "renderSeconds": round(time.time() - t0, 1), "video": rep["video"],
                       "credits": nar.get("credits")})
            summary["credits"] += nar.get("credits") or 0
            write_json(state_path, state)
            log(cfg, {"event": "assembled", "key": key, "detail": f"{rep['seconds']}s video in {st['renderSeconds']}s"})
            if args.stream:
                shutil.rmtree(ldir.segments, ignore_errors=True)
            if args.upload:
                from .youtube import upload_lecture_dir

                res = upload_lecture_dir(cfg, ldir.out)
                st.update({"stage": "uploaded", "videoId": res["videoId"]})
                write_json(state_path, state)
                log(cfg, {"event": "uploaded", "key": key, "detail": res["url"]})
                if args.stream:
                    (ldir.out / "video.mp4").unlink(missing_ok=True)
            summary["done"] += 1
        except Exception as exc:  # keep the batch going; the state file lets a rerun resume
            summary["failed"] += 1
            st["error"] = str(exc)[:500]
            write_json(state_path, state)
            log(cfg, {"event": "failed", "key": key, "detail": str(exc)[:300]})
    summary["credits"] = round(summary["credits"], 2)
    print(json.dumps(summary, indent=2))
    return summary
