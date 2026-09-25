"""`narrate`: narration audio per scene, content-addressed so a rerun never pays twice.

Two backends produce the same cache entries (cache/tts/<sha256(voice+model+text)>.<ext> + .json sidecar):

* ``mcp`` (default, used for the pilot): the agent calls the ElevenLabs MCP tool
  ``creative_generate_speech`` (generations_count=1, one flow per lecture), polls
  ``creative_get_flow_run_status`` and then runs ``narrate ingest`` with the returned signed URL.
  ``narrate requests`` prints exactly which scenes still need audio and the text to send.
* ``api``: unattended batch mode against the ElevenLabs REST API with ELEVENLABS_API_KEY from the
  environment (``/v1/text-to-speech/{voice}/with-timestamps``), which also returns character timings.
"""
from __future__ import annotations

import base64
import json
import os
import time
import urllib.parse
from pathlib import Path

from .audio import exact_duration
from .config import Config
from .workspace import LectureDir, read_json, write_json

ALLOWED_DOWNLOAD_HOSTS = ("storage.googleapis.com",)
ELEVEN_API = "https://api.elevenlabs.io"


class NarrationError(RuntimeError):
    pass


def cache_paths(cfg: Config, key: str) -> tuple[Path | None, Path]:
    meta = cfg.audio_cache / f"{key}.json"
    for ext in ("mp3", "wav", "m4a", "ogg", "opus", "flac"):
        p = cfg.audio_cache / f"{key}.{ext}"
        if p.exists():
            return p, meta
    return None, meta


def status(cfg: Config, plan: dict) -> list[dict]:
    rows = []
    for sc in plan["scenes"]:
        audio, meta = cache_paths(cfg, sc["ttsKey"])
        rows.append(
            {
                "scene": sc["id"],
                "key": sc["ttsKey"],
                "cached": audio is not None,
                "chars": len(sc["tts"]),
                "text": sc["tts"],
            }
        )
    return rows


def requests_for_agent(cfg: Config, plan: dict) -> dict:
    """What the agent must send to creative_generate_speech (only uncached scenes)."""
    missing = [r for r in status(cfg, plan) if not r["cached"]]
    return {
        "lecture": plan["key"],
        "voice_id": plan["voice"]["id"],
        "voice_name": plan["voice"]["name"],
        "model_id": plan["model"],
        "generations_count": 1,
        "missing": missing,
        "missingCharacters": sum(r["chars"] for r in missing),
    }


def _check_url(url: str) -> None:
    u = urllib.parse.urlparse(url)
    if u.scheme != "https" or u.hostname not in ALLOWED_DOWNLOAD_HOSTS:
        raise NarrationError(f"refusing to download from {u.scheme}://{u.hostname} (allowed: {ALLOWED_DOWNLOAD_HOSTS})")


def _ext_for(url: str, content_type: str | None) -> str:
    path = urllib.parse.urlparse(url).path.lower()
    for ext in ("mp3", "wav", "m4a", "ogg", "opus", "flac"):
        if path.endswith("." + ext):
            return ext
    ct = (content_type or "").lower()
    if "wav" in ct:
        return "wav"
    if "ogg" in ct or "opus" in ct:
        return "ogg"
    if "mp4" in ct or "m4a" in ct or "aac" in ct:
        return "m4a"
    return "mp3"


def _store(cfg: Config, key: str, data: bytes, ext: str, meta: dict) -> Path:
    cfg.audio_cache.mkdir(parents=True, exist_ok=True)
    dest = cfg.audio_cache / f"{key}.{ext}"
    tmp = dest.with_suffix(dest.suffix + ".part")
    tmp.write_bytes(data)
    try:
        meta["seconds"] = round(exact_duration(tmp), 4)
    except Exception as exc:
        tmp.unlink(missing_ok=True)
        raise NarrationError(f"downloaded audio does not decode: {exc}") from exc
    os.replace(tmp, dest)
    meta["bytes"] = len(data)
    meta["file"] = dest.name
    write_json(cfg.audio_cache / f"{key}.json", meta)
    return dest


def ingest(cfg: Config, plan: dict, scene_id: str, url: str, *, credits: float | None = None,
           flow_id: str | None = None, session_id: str | None = None, http_get=None) -> dict:
    """Download one generated clip (signed storage.googleapis.com URL) into the cache."""
    sc = next((s for s in plan["scenes"] if s["id"] == scene_id), None)
    if sc is None:
        raise NarrationError(f"no scene {scene_id} in {plan['key']}")
    existing, _ = cache_paths(cfg, sc["ttsKey"])
    if existing:
        return {"scene": scene_id, "cached": True, "file": str(existing)}
    _check_url(url)
    if http_get is None:
        import requests

        def http_get(u):
            r = requests.get(u, timeout=120)
            r.raise_for_status()
            return r.content, r.headers.get("content-type")

    data, ctype = http_get(url)
    if len(data) < 1024:
        raise NarrationError(f"audio for {scene_id} is suspiciously small ({len(data)} bytes)")
    meta = {
        "key": sc["ttsKey"],
        "voice": plan["voice"]["id"],
        "model": plan["model"],
        "chars": len(sc["tts"]),
        "text": sc["tts"],
        "backend": "mcp",
        "credits": credits,
        "flowId": flow_id,
        "sessionId": session_id,
        "createdAt": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
    }
    dest = _store(cfg, sc["ttsKey"], data, _ext_for(url, ctype), meta)
    return {"scene": scene_id, "cached": False, "file": str(dest), "seconds": meta["seconds"]}


def synthesize_api(cfg: Config, plan: dict, *, api_key: str | None = None, http_post=None, dry_run: bool = False) -> list[dict]:
    """Unattended backend: ElevenLabs REST with timestamps. One request per uncached scene, never retried blindly."""
    api_key = api_key or os.environ.get("ELEVENLABS_API_KEY")
    todo = [s for s in plan["scenes"] if cache_paths(cfg, s["ttsKey"])[0] is None]
    if dry_run:
        return [{"scene": s["id"], "chars": len(s["tts"]), "dryRun": True} for s in todo]
    if todo and not api_key:
        raise NarrationError("ELEVENLABS_API_KEY is not set (or use the MCP flow: narrate requests / ingest)")
    if http_post is None:
        import requests

        def http_post(url, headers, body):
            r = requests.post(url, headers=headers, json=body, timeout=300)
            if r.status_code >= 400:
                raise NarrationError(f"ElevenLabs HTTP {r.status_code}: {r.text[:300]}")
            return r.json(), r.headers

    out = []
    for s in todo:
        url = f"{ELEVEN_API}/v1/text-to-speech/{plan['voice']['id']}/with-timestamps?output_format=mp3_44100_128"
        body = {"text": s["tts"], "model_id": plan["model"]}
        payload, headers = http_post(url, {"xi-api-key": api_key, "content-type": "application/json"}, body)
        audio = base64.b64decode(payload["audio_base64"])
        meta = {
            "key": s["ttsKey"],
            "voice": plan["voice"]["id"],
            "model": plan["model"],
            "chars": len(s["tts"]),
            "text": s["tts"],
            "backend": "api",
            "credits": _float(headers.get("character-cost") if headers else None),
            "requestId": headers.get("request-id") if headers else None,
            "alignment": payload.get("alignment"),
            "createdAt": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        }
        _store(cfg, s["ttsKey"], audio, "mp3", meta)
        out.append({"scene": s["id"], "chars": len(s["tts"]), "credits": meta["credits"]})
    return out


def _float(v):
    try:
        return float(v)
    except (TypeError, ValueError):
        return None


def collect(cfg: Config, plan: dict, ldir: LectureDir) -> dict:
    """Write narration.json for the lecture (paths, durations, credits). Raises if any scene lacks audio."""
    scenes, missing, credits = [], [], 0.0
    for sc in plan["scenes"]:
        audio, meta_path = cache_paths(cfg, sc["ttsKey"])
        if audio is None:
            missing.append(sc["id"])
            continue
        meta = read_json(meta_path, {}) or {}
        if meta.get("seconds") is None:
            meta["seconds"] = round(exact_duration(audio), 4)
        credits += meta.get("credits") or 0.0
        scenes.append(
            {
                "id": sc["id"],
                "file": str(audio),
                "seconds": meta["seconds"],
                "credits": meta.get("credits"),
                "alignment": meta.get("alignment"),
            }
        )
    if missing:
        raise NarrationError(f"{plan['key']}: no audio yet for {', '.join(missing)} (run `narrate requests`)")
    result = {"key": plan["key"], "scenes": scenes, "credits": round(credits, 2),
              "seconds": round(sum(s["seconds"] for s in scenes), 3)}
    write_json(ldir.narration, result)
    return result


def ledger_summary(cfg: Config) -> dict:
    """Credits and characters across the whole cache (for cost reporting)."""
    total_chars = total_credits = total_secs = 0.0
    n = 0
    for meta in sorted(cfg.audio_cache.glob("*.json")):
        m = json.loads(meta.read_text(encoding="utf-8"))
        n += 1
        total_chars += m.get("chars") or 0
        total_credits += m.get("credits") or 0
        total_secs += m.get("seconds") or 0
    return {"clips": n, "characters": int(total_chars), "credits": round(total_credits, 2), "audioSeconds": round(total_secs, 1)}
