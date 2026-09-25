"""CLI entry point: python3 -m studio <command> ...  (run from tools/lecture-studio)."""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from .config import Config
from .packs import find_lecture, load_pack
from .plan import build_plan
from .workspace import LectureDir, read_json, write_json


def _cfg(args) -> Config:
    cfg = Config.load(args.config)
    if args.catalog:
        cfg.catalog_dir = Path(args.catalog)
    if args.work:
        cfg.work_dir = Path(args.work)
    if getattr(args, "site", None):
        cfg.site_base_url = args.site
    return cfg


def _plan(cfg: Config, key: str, *, refresh: bool = True) -> tuple[dict, LectureDir]:
    course, lesson = key.split("/", 1)
    ldir = LectureDir(cfg, key)
    if refresh or not ldir.plan.exists():
        ref = find_lecture(load_pack(cfg.catalog_dir, course), lesson)
        plan = build_plan(ref, cfg)
        write_json(ldir.plan, plan)
    return read_json(ldir.plan), ldir


def _print(obj) -> None:
    print(json.dumps(obj, indent=2, ensure_ascii=False))


def cmd_plan(args):
    cfg = _cfg(args)
    plan, ldir = _plan(cfg, args.lecture)
    if args.summary:
        for s in plan["scenes"]:
            print(f"{s['id']}  {s['template']:8s} {s['chapter']}")
        print(f"voice={plan['voice']['name']} chars={plan['ttsCharacters']}  -> {ldir.plan}")
    else:
        _print(plan)


def cmd_narrate(args):
    from . import narrate

    cfg = _cfg(args)
    if args.action == "ledger":
        _print(narrate.ledger_summary(cfg))
        return
    if not args.lecture:
        sys.exit("narrate needs course/lesson")
    plan, ldir = _plan(cfg, args.lecture)
    if args.action == "requests":
        _print(narrate.requests_for_agent(cfg, plan))
    elif args.action == "status":
        rows = narrate.status(cfg, plan)
        for r in rows:
            print(f"{r['scene']}  {'cached ' if r['cached'] else 'MISSING'} {r['chars']:5d} chars  {r['key'][:12]}")
    elif args.action == "ingest":
        if not (args.scene and args.url):
            sys.exit("ingest needs --scene and --url")
        _print(narrate.ingest(cfg, plan, args.scene, args.url, credits=args.credits,
                              flow_id=args.flow, session_id=args.session))
    elif args.action == "ingest-batch":
        # JSON file: {"flow": "...", "scenes": {"s01": {"url": "...", "credits": 386.9, "session": "..."}}}
        batch = json.loads(Path(args.file).read_text(encoding="utf-8"))
        for sid, item in sorted(batch["scenes"].items()):
            res = narrate.ingest(cfg, plan, sid, item["url"], credits=item.get("credits"),
                                 flow_id=batch.get("flow"), session_id=item.get("session"))
            print(f"{sid}: {'cached' if res['cached'] else 'downloaded'} {res.get('seconds', '')}")
    elif args.action == "api":
        _print(narrate.synthesize_api(cfg, plan, dry_run=args.dry_run))
    elif args.action == "collect":
        res = narrate.collect(cfg, plan, ldir)
        _print({k: v for k, v in res.items() if k != "scenes"})
    elif args.action == "ledger":
        _print(narrate.ledger_summary(cfg))


def cmd_render(args):
    from . import render

    cfg = _cfg(args)
    plan, ldir = _plan(cfg, args.lecture)
    render.render_lecture(cfg, plan, ldir, workers=args.workers, only=args.only, stills=args.stills,
                          force=args.force, burn_captions=args.burn_captions, stills_only=args.stills_only,
                          preview=args.preview)


def cmd_assemble(args):
    from . import assemble

    cfg = _cfg(args)
    plan, ldir = _plan(cfg, args.lecture)
    _print(assemble.assemble(cfg, plan, ldir, burn_captions=args.burn_captions, out_dir=args.out))


def cmd_upload(args):
    from . import youtube

    cfg = _cfg(args)
    _print(youtube.upload_lecture_dir(cfg, Path(args.dir), dry_run=args.dry_run, ledger=Path(args.ledger) if args.ledger else None))


def cmd_run(args):
    from . import run

    cfg = _cfg(args)
    run.run_batch(cfg, args, lectures=args.lectures)


def main(argv=None):
    p = argparse.ArgumentParser(prog="studio", description="Optimize All Lecture Studio")
    p.add_argument("--config", help="JSON config override (default: studio.config.json if present)")
    p.add_argument("--catalog", help="course pack directory (default: repo catalog or $LECTURE_STUDIO_CATALOG)")
    p.add_argument("--work", help="work/cache directory (default: tools/lecture-studio/.work or $LECTURE_STUDIO_WORK)")
    p.add_argument("--site", help="public site base URL used for lesson links")
    sub = p.add_subparsers(dest="cmd", required=True)

    sp = sub.add_parser("plan", help="build the render plan for course/lesson")
    sp.add_argument("lecture", help="course-slug/lesson-slug")
    sp.add_argument("--summary", action="store_true")
    sp.set_defaults(fn=cmd_plan)

    sp = sub.add_parser("narrate", help="narration audio: requests|status|ingest|api|collect|ledger")
    sp.add_argument("action", choices=["requests", "status", "ingest", "ingest-batch", "api", "collect", "ledger"])
    sp.add_argument("lecture", nargs="?")
    sp.add_argument("--scene")
    sp.add_argument("--url")
    sp.add_argument("--credits", type=float)
    sp.add_argument("--flow")
    sp.add_argument("--session")
    sp.add_argument("--file", help="ingest-batch: JSON with per-scene signed URLs")
    sp.add_argument("--dry-run", action="store_true")
    sp.set_defaults(fn=cmd_narrate)

    sp = sub.add_parser("render", help="render scene video segments (Playwright, frame-accurate)")
    sp.add_argument("lecture")
    sp.add_argument("--workers", type=int)
    sp.add_argument("--only", help="comma-separated scene ids (plus 'intro','outro')")
    sp.add_argument("--stills", action="store_true", help="also export a PNG still per scene")
    sp.add_argument("--force", action="store_true", help="re-render even when the segment is up to date")
    sp.add_argument("--stills-only", action="store_true", help="only export review stills (no video)")
    sp.add_argument("--preview", action="store_true", help="design preview: stills from estimated timing, no audio needed")
    sp.add_argument("--burn-captions", action="store_true", help="draw captions into the picture")
    sp.set_defaults(fn=cmd_render)

    sp = sub.add_parser("assemble", help="concat + audio + loudnorm -> MP4, poster, VTT, YouTube metadata")
    sp.add_argument("lecture")
    sp.add_argument("--burn-captions", action="store_true")
    sp.add_argument("--out", help="copy final deliverables into this directory")
    sp.set_defaults(fn=cmd_assemble)

    sp = sub.add_parser("upload", help="upload one assembled lecture directory to YouTube")
    sp.add_argument("dir", help="directory containing video.mp4, poster.png, captions.vtt, youtube.json")
    sp.add_argument("--ledger")
    sp.add_argument("--dry-run", action="store_true")
    sp.set_defaults(fn=cmd_upload)

    sp = sub.add_parser("run", help="batch orchestrator (plan -> narrate -> render -> assemble -> upload)")
    sp.add_argument("lectures", nargs="*", help="course/lesson keys or course slugs (all lectures of the course)")
    sp.add_argument("--all-v2", action="store_true", help="every lecture in every v2 pack")
    sp.add_argument("--narrate-backend", choices=["cache-only", "api"], default="cache-only")
    sp.add_argument("--upload", action="store_true")
    sp.add_argument("--stream", action="store_true", help="delete local MP4s after a successful upload")
    sp.add_argument("--dry-run", action="store_true")
    sp.add_argument("--limit", type=int)
    sp.add_argument("--workers", type=int)
    sp.add_argument("--out", help="copy deliverables here")
    sp.set_defaults(fn=cmd_run)

    args = p.parse_args(argv)
    args.fn(args)


if __name__ == "__main__":
    main()
