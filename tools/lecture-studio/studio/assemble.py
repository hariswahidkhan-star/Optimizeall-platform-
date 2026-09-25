"""`assemble`: segments + narration -> final MP4 (H.264/AAC, -16 LUFS), poster PNG, WebVTT, YouTube metadata."""
from __future__ import annotations

import json
import re
import shutil
import subprocess
import time
import wave
from pathlib import Path

from . import captions, metadata
from .audio import SAMPLE_RATE, measure_loudness, run_ffmpeg
from .config import Config
from .packs import load_pack
from .render import RENDERER, TOOL_DIR, fonts
from .workspace import LectureDir, read_json, write_json

SAMPLES_PER_FRAME_48K = {24: 2000, 25: 1920, 30: 1600, 60: 800}


def _decode_pcm(path: Path) -> bytes:
    res = run_ffmpeg(["-i", str(path), "-vn", "-ac", "1", "-ar", str(SAMPLE_RATE), "-f", "s16le", "-"])
    return res.stdout


def build_narration_wav(cfg: Config, timeline: dict, out: Path) -> float:
    """Sample-exact narration track: silence for intro/outro, each clip placed at lead-in inside its scene."""
    spf = SAMPLES_PER_FRAME_48K.get(cfg.fps) or int(SAMPLE_RATE / cfg.fps)
    pcm = bytearray()
    pcm += b"\x00\x00" * (timeline["intro"]["frames"] * spf)
    for sc in timeline["scenes"]:
        n = sc["frames"] * spf
        lead = int(round(sc["audioStart"] * SAMPLE_RATE))
        clip = _decode_pcm(Path(sc["audioFile"]))
        seg = bytearray(b"\x00\x00" * lead) + clip
        if len(seg) > n * 2:
            seg = seg[: n * 2]  # never happens with tail > 0, but stay frame-exact
        seg += b"\x00\x00" * (n - len(seg) // 2)
        pcm += seg
    pcm += b"\x00\x00" * (timeline["outro"]["frames"] * spf)
    out.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(out), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SAMPLE_RATE)
        w.writeframes(bytes(pcm))
    return len(pcm) / 2 / SAMPLE_RATE


def loudnorm_filter(cfg: Config, measured: dict) -> str:
    """Second loudnorm pass with measured values (linear gain when possible: no pumping on speech)."""
    return (
        f"loudnorm=I={cfg.loudness_lufs}:TP={cfg.true_peak}:LRA=11:"
        f"measured_I={measured['input_i']}:measured_TP={measured['input_tp']}:"
        f"measured_LRA={measured['input_lra']}:measured_thresh={measured['input_thresh']}:"
        f"offset={measured['target_offset']}:linear=true:print_format=summary"
    )


def render_poster(cfg: Config, plan: dict, ldir: LectureDir, minutes: int) -> Path:
    cat = (plan["course"].get("category") or "course").upper()
    labels = {"AI": "AI", "SEO": "SEO", "DATA": "Data", "MARKETING": "Marketing", "SALES": "Sales", "BUSINESS": "Business",
              "DESIGN": "Design", "PLATFORM": "Platform"}
    scene = {
        "kind": "poster", "id": "poster", "duration": 1, "frames": 1,
        "course": plan["course"], "lesson": plan["lesson"], "module": plan["module"], "lectureTitle": plan["lectureTitle"],
        "categoryLabel": labels.get(cat, cat.title()), "minutes": minutes,
    }
    raw = ldir.root / "stills" / "poster-1080.png"
    job = {
        "width": cfg.width, "height": cfg.height, "fps": cfg.fps, "workers": 1, "ffmpeg": "", "fonts": fonts(cfg),
        "nodeModules": str(cfg.node_modules), "stillsOnly": True,
        "items": [{"id": "poster", "out": str(ldir.root / "poster.mp4"), "scene": scene, "frames": 1, "stills": [{"t": 0.5, "out": str(raw)}]}],
    }
    job_path = ldir.root / "render-poster.json"
    write_json(job_path, job)
    res = subprocess.run(["node", str(RENDERER / "render.mjs"), str(job_path)], cwd=str(TOOL_DIR), capture_output=True, text=True)
    if res.returncode != 0:
        raise RuntimeError(f"poster render failed: {res.stderr[-800:]}")
    out = ldir.output("poster.png")
    run_ffmpeg(["-y", "-i", str(raw), "-vf", "scale=1280:720:flags=lanczos", str(out)])
    if out.stat().st_size > 2 * 1024 * 1024:  # YouTube thumbnail limit
        jpg = ldir.output("poster.jpg")
        run_ffmpeg(["-y", "-i", str(raw), "-vf", "scale=1280:720:flags=lanczos", "-q:v", "3", str(jpg)])
        return jpg
    return out


def assemble(cfg: Config, plan: dict, ldir: LectureDir, *, out_dir: str | None = None, burn_captions: bool = False) -> dict:
    t0 = time.time()
    timeline = read_json(ldir.timeline)
    narration = read_json(ldir.narration)
    if not timeline or not narration:
        raise RuntimeError("run `render` first (timeline.json / narration.json missing)")
    ids = ["intro", *[s["id"] for s in timeline["scenes"]], "outro"]
    missing = [i for i in ids if not (ldir.segments / f"{i}.mp4").exists()]
    if missing:
        raise RuntimeError(f"missing rendered segments: {missing}")
    # 1) video: concat identical-parameter segments (stream copy)
    concat_list = ldir.root / "concat.txt"
    concat_list.write_text("".join(f"file '{(ldir.segments / f'{i}.mp4').as_posix()}'\n" for i in ids), encoding="utf-8")
    silent = ldir.root / "video-silent.mp4"
    run_ffmpeg(["-y", "-f", "concat", "-safe", "0", "-i", str(concat_list), "-c", "copy", str(silent)])
    # 2) audio: sample-exact narration track, then two-pass EBU R128 normalisation to -16 LUFS
    wav = ldir.root / "narration.wav"
    audio_secs = build_narration_wav(cfg, timeline, wav)
    measured = measure_loudness(wav)
    # 3) final encode: progress bar overlay (hidden during intro/outro), AAC 48 kHz stereo
    total = timeline["totalSeconds"]
    a, b = timeline["intro"]["duration"], timeline["outro"]["start"]
    bar = (
        f"color=c=0xFCB31E:s={cfg.width}x6:r={cfg.fps}[bar];"
        f"[0:v][bar]overlay=x='-w+w*min(1,max(0,(t-{a:.3f})/{b - a:.3f}))':y=H-6:shortest=1:"
        f"enable='between(t,{a:.3f},{b:.3f})'[v]"
    )
    video = ldir.output("video.mp4")
    tmp = video.with_suffix(".part.mp4")
    run_ffmpeg([
        "-y", "-i", str(silent), "-i", str(wav),
        "-filter_complex", f"{bar};[1:a]{loudnorm_filter(cfg, measured)},aresample=48000,aformat=channel_layouts=stereo[a]",
        "-map", "[v]", "-map", "[a]",
        "-c:v", "libx264", "-preset", "medium", "-crf", "19", "-tune", "animation", "-pix_fmt", "yuv420p",
        "-profile:v", "high", "-level", "4.1", "-g", str(cfg.fps * 2), "-bf", "2",
        "-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "bt709", "-color_range", "tv",
        "-c:a", "aac", "-b:a", "192k", "-ar", "48000",
        "-movflags", "+faststart", "-t", f"{total:.3f}", str(tmp),
    ])
    tmp.replace(video)
    final_loud = measure_loudness(video)
    # 4) captions, poster, metadata
    cues = captions.cues_from_timeline(timeline)
    vtt = ldir.output("captions.vtt")
    vtt.write_text(captions.to_vtt(cues), encoding="utf-8")
    minutes = max(1, round(total / 60))
    poster = render_poster(cfg, plan, ldir, minutes)
    pack = load_pack(cfg.catalog_dir, plan["course"]["slug"])
    meta = metadata.build(cfg, plan, timeline, pack, credits=narration.get("credits"))
    meta["files"] = {"video": video.name, "thumbnail": poster.name, "captions": vtt.name}
    write_json(ldir.output("youtube.json"), meta)
    for f in (silent, wav):
        f.unlink(missing_ok=True)
    report = {
        "key": plan["key"],
        "video": str(video),
        "bytes": video.stat().st_size,
        "seconds": round(total, 2),
        "audioSeconds": round(audio_secs, 2),
        "loudness": {"integratedLUFS": final_loud["input_i"], "truePeak": final_loud["input_tp"], "lra": final_loud["input_lra"]},
        "poster": str(poster),
        "captions": str(vtt),
        "cues": len(cues),
        "metadata": str(ldir.output("youtube.json")),
        "credits": narration.get("credits"),
        "assembleSeconds": round(time.time() - t0, 1),
    }
    write_json(ldir.output("report.json"), report)
    if out_dir:
        dest = Path(out_dir) / plan["key"].replace("/", "__")
        dest.mkdir(parents=True, exist_ok=True)
        for f in ldir.out.iterdir():
            shutil.copy2(f, dest / f.name)
        report["copiedTo"] = str(dest)
    return report
