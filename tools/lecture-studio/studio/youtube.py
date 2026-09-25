"""`upload`: YouTube Data API v3 publisher (resumable upload, thumbnail, captions, playlist), idempotent via a ledger.

Credentials come ONLY from the environment: YOUTUBE_CLIENT_ID, YOUTUBE_CLIENT_SECRET, YOUTUBE_REFRESH_TOKEN
(an OAuth 2.0 refresh token for the channel owner with the https://www.googleapis.com/auth/youtube.upload and
https://www.googleapis.com/auth/youtube.force-ssl scopes). Nothing secret is ever logged or written to disk.

The HTTP layer is injectable (``Http``) so request building is unit-tested without the network.
"""
from __future__ import annotations

import json
import os
import random
import time
import uuid
from dataclasses import dataclass
from pathlib import Path

from .config import Config
from .workspace import read_json, write_json

TOKEN_URL = "https://oauth2.googleapis.com/token"
API = "https://www.googleapis.com/youtube/v3"
UPLOAD_API = "https://www.googleapis.com/upload/youtube/v3"
CHUNK = 8 * 1024 * 1024  # must be a multiple of 256 KiB
RETRY_STATUS = {500, 502, 503, 504}
# Quota units per call (general bucket). videos.insert is billed separately (see README for the current rules).
QUOTA = {"captions.insert": 400, "thumbnails.set": 50, "playlistItems.insert": 50, "playlists.insert": 50,
         "playlists.list": 1, "videos.list": 1}


class YouTubeError(RuntimeError):
    def __init__(self, msg: str, status: int | None = None, reason: str | None = None):
        super().__init__(msg)
        self.status = status
        self.reason = reason


@dataclass
class Resp:
    status: int
    headers: dict
    body: bytes

    def json(self):
        return json.loads(self.body.decode("utf-8") or "{}")


class Http:
    """Minimal requests wrapper (swapped for a fake in tests)."""

    def request(self, method: str, url: str, *, headers=None, params=None, data=None, timeout=300) -> Resp:
        import requests

        r = requests.request(method, url, headers=headers, params=params, data=data, timeout=timeout, allow_redirects=False)
        return Resp(r.status_code, {k.lower(): v for k, v in r.headers.items()}, r.content)


def _redact(text: str) -> str:
    for var in ("YOUTUBE_CLIENT_SECRET", "YOUTUBE_REFRESH_TOKEN", "YOUTUBE_CLIENT_ID"):
        v = os.environ.get(var)
        if v:
            text = text.replace(v, "***")
    return text


def _error(resp: Resp, what: str) -> YouTubeError:
    reason = None
    try:
        err = resp.json().get("error", {})
        errs = err.get("errors") or [{}]
        reason = errs[0].get("reason") or err.get("status")
        msg = err.get("message") or ""
    except Exception:
        msg = resp.body[:300].decode("utf-8", "replace")
    return YouTubeError(_redact(f"{what} failed: HTTP {resp.status} {reason or ''} {msg}".strip()), resp.status, reason)


class YouTube:
    def __init__(self, http: Http | None = None, *, client_id=None, client_secret=None, refresh_token=None,
                 sleep=time.sleep, max_retries: int = 6):
        self.http = http or Http()
        self.client_id = client_id or os.environ.get("YOUTUBE_CLIENT_ID")
        self.client_secret = client_secret or os.environ.get("YOUTUBE_CLIENT_SECRET")
        self.refresh_token = refresh_token or os.environ.get("YOUTUBE_REFRESH_TOKEN")
        self.sleep = sleep
        self.max_retries = max_retries
        self._token = None
        self._token_exp = 0.0
        self.quota_used = 0

    # ------------------------------------------------------------------ auth
    def token(self) -> str:
        if self._token and time.time() < self._token_exp - 60:
            return self._token
        missing = [n for n, v in (("YOUTUBE_CLIENT_ID", self.client_id), ("YOUTUBE_CLIENT_SECRET", self.client_secret),
                                  ("YOUTUBE_REFRESH_TOKEN", self.refresh_token)) if not v]
        if missing:
            raise YouTubeError(f"missing environment variables: {', '.join(missing)}")
        resp = self.http.request("POST", TOKEN_URL, headers={"content-type": "application/x-www-form-urlencoded"}, data={
            "client_id": self.client_id, "client_secret": self.client_secret,
            "refresh_token": self.refresh_token, "grant_type": "refresh_token",
        })
        if resp.status != 200:
            raise _error(resp, "OAuth token refresh")
        body = resp.json()
        self._token = body["access_token"]
        self._token_exp = time.time() + float(body.get("expires_in", 3600))
        return self._token

    def _auth(self, extra=None) -> dict:
        h = {"authorization": f"Bearer {self.token()}"}
        if extra:
            h.update(extra)
        return h

    # ------------------------------------------------------------------ transport with backoff
    def call(self, method: str, url: str, *, what: str, params=None, headers=None, data=None, ok=(200,)) -> Resp:
        for attempt in range(self.max_retries + 1):
            resp = self.http.request(method, url, headers=self._auth(headers), params=params, data=data)
            if resp.status in ok:
                self.quota_used += QUOTA.get(what, 0)
                return resp
            if resp.status == 401 and attempt == 0:
                self._token = None  # expired token: refresh once
                continue
            err = _error(resp, what)
            if err.reason in ("quotaExceeded", "dailyLimitExceeded", "uploadLimitExceeded"):
                raise err  # retrying cannot help until the quota resets (midnight Pacific time)
            retriable = resp.status in RETRY_STATUS or err.reason in ("rateLimitExceeded", "userRateLimitExceeded", "backendError")
            if not retriable or attempt == self.max_retries:
                raise err
            self.sleep(min(64, 2 ** attempt) + random.random())
        raise YouTubeError(f"{what}: retries exhausted")

    # ------------------------------------------------------------------ resumable upload
    def start_upload(self, meta: dict, size: int) -> str:
        body = json.dumps({"snippet": meta["snippet"], "status": meta["status"]}).encode("utf-8")
        resp = self.call(
            "POST", f"{UPLOAD_API}/videos", what="videos.insert",
            params={"uploadType": "resumable", "part": "snippet,status", "notifySubscribers": "false"},
            headers={"content-type": "application/json; charset=UTF-8", "x-upload-content-length": str(size),
                     "x-upload-content-type": "video/mp4"},
            data=body,
        )
        loc = resp.headers.get("location")
        if not loc:
            raise YouTubeError("resumable session did not return a Location header")
        return loc

    def upload_status(self, session_url: str, size: int) -> tuple[int, dict | None]:
        """Ask the server how many bytes it has (for resuming). Returns (next_offset, video or None)."""
        resp = self.http.request("PUT", session_url, headers=self._auth({"content-range": f"bytes */{size}", "content-length": "0"}))
        if resp.status in (200, 201):
            return size, resp.json()
        if resp.status == 308:
            rng = resp.headers.get("range")
            return (int(rng.split("-")[1]) + 1 if rng else 0), None
        if resp.status in (404, 410):
            return -1, None  # session expired: start a new one
        raise _error(resp, "upload status")

    def upload_file(self, session_url: str, path: Path, *, offset: int = 0, progress=None) -> dict:
        size = path.stat().st_size
        with path.open("rb") as fh:
            while True:
                fh.seek(offset)
                chunk = fh.read(CHUNK)
                end = offset + len(chunk) - 1
                for attempt in range(self.max_retries + 1):
                    try:
                        resp = self.http.request("PUT", session_url, headers=self._auth({
                            "content-length": str(len(chunk)), "content-range": f"bytes {offset}-{end}/{size}",
                            "content-type": "video/mp4"}), data=chunk)
                    except Exception:  # network drop: ask the server where we are
                        resp = None
                    if resp is not None and resp.status in (200, 201):
                        return resp.json()
                    if resp is not None and resp.status == 308:
                        rng = resp.headers.get("range")
                        offset = int(rng.split("-")[1]) + 1 if rng else 0
                        break
                    if resp is not None and resp.status not in RETRY_STATUS:
                        raise _error(resp, "video upload")
                    if attempt == self.max_retries:
                        raise YouTubeError("video upload: retries exhausted")
                    self.sleep(min(64, 2 ** attempt) + random.random())
                    offset, video = self.upload_status(session_url, size)
                    if video:
                        return video
                    if offset < 0:
                        raise YouTubeError("upload session expired", 410)
                    break
                if progress:
                    progress(offset, size)

    # ------------------------------------------------------------------ other resources
    def set_thumbnail(self, video_id: str, path: Path) -> None:
        ctype = "image/png" if path.suffix.lower() == ".png" else "image/jpeg"
        self.call("POST", f"{UPLOAD_API}/thumbnails/set", what="thumbnails.set",
                  params={"videoId": video_id, "uploadType": "media"}, headers={"content-type": ctype}, data=path.read_bytes())

    @staticmethod
    def caption_body(video_id: str, vtt: bytes, language: str, name: str, boundary: str | None = None) -> tuple[bytes, str]:
        boundary = boundary or f"oa{uuid.uuid4().hex}"
        meta = json.dumps({"snippet": {"videoId": video_id, "language": language, "name": name, "isDraft": False}})
        parts = [
            f"--{boundary}\r\nContent-Type: application/json; charset=UTF-8\r\n\r\n{meta}\r\n".encode(),
            f"--{boundary}\r\nContent-Type: text/vtt\r\n\r\n".encode() + vtt + b"\r\n",
            f"--{boundary}--\r\n".encode(),
        ]
        return b"".join(parts), f"multipart/related; boundary={boundary}"

    def insert_captions(self, video_id: str, path: Path, language: str, name: str) -> str:
        body, ctype = self.caption_body(video_id, path.read_bytes(), language, name)
        resp = self.call("POST", f"{UPLOAD_API}/captions", what="captions.insert",
                         params={"uploadType": "multipart", "part": "snippet"}, headers={"content-type": ctype}, data=body)
        return resp.json().get("id", "")

    def find_playlist(self, title: str) -> str | None:
        page = None
        while True:
            params = {"part": "snippet", "mine": "true", "maxResults": "50"}
            if page:
                params["pageToken"] = page
            data = self.call("GET", f"{API}/playlists", what="playlists.list", params=params).json()
            for it in data.get("items", []):
                if it["snippet"]["title"] == title:
                    return it["id"]
            page = data.get("nextPageToken")
            if not page:
                return None

    def create_playlist(self, title: str, description: str, privacy: str) -> str:
        body = json.dumps({"snippet": {"title": title[:150], "description": description[:5000]}, "status": {"privacyStatus": privacy}})
        resp = self.call("POST", f"{API}/playlists", what="playlists.insert", params={"part": "snippet,status"},
                         headers={"content-type": "application/json"}, data=body.encode())
        return resp.json()["id"]

    def add_to_playlist(self, playlist_id: str, video_id: str, position: int | None = None) -> str:
        snippet = {"playlistId": playlist_id, "resourceId": {"kind": "youtube#video", "videoId": video_id}}
        if position is not None:
            snippet["position"] = position
        resp = self.call("POST", f"{API}/playlistItems", what="playlistItems.insert", params={"part": "snippet"},
                         headers={"content-type": "application/json"}, data=json.dumps({"snippet": snippet}).encode())
        return resp.json().get("id", "")


# ---------------------------------------------------------------------- ledger-driven publish

def default_ledger(cfg: Config) -> Path:
    return cfg.work_dir / "youtube-ledger.json"


def upload_lecture_dir(cfg: Config, d: Path, *, dry_run: bool = False, ledger: Path | None = None,
                       yt: YouTube | None = None, patch_file: Path | None = None) -> dict:
    """Publish one assembled lecture (video.mp4, poster, captions.vtt, youtube.json). Idempotent per lecture key:
    each finished step is recorded in the ledger, so a rerun resumes and never uploads a video twice."""
    meta = json.loads((d / "youtube.json").read_text(encoding="utf-8"))
    key = meta["key"]
    files = meta.get("files", {})
    video = d / files.get("video", "video.mp4")
    thumb = d / files.get("thumbnail", "poster.png")
    vtt = d / files.get("captions", "captions.vtt")
    ledger = ledger or default_ledger(cfg)
    book = read_json(ledger, {"lectures": {}, "playlists": {}}) or {"lectures": {}, "playlists": {}}
    entry = book["lectures"].setdefault(key, {})
    plan = ["video" if not entry.get("videoId") else None,
            "thumbnail" if not entry.get("thumbnail") else None,
            "captions" if not entry.get("captionId") else None,
            "playlist" if not entry.get("playlistItemId") else None]
    todo = [p for p in plan if p]
    if dry_run:
        return {"key": key, "dryRun": True, "todo": todo, "title": meta["snippet"]["title"],
                "estimatedGeneralQuota": (400 if "captions" in todo else 0) + (50 if "thumbnail" in todo else 0) + (50 if "playlist" in todo else 0)}
    yt = yt or YouTube()

    def save():
        write_json(ledger, book)

    if not entry.get("videoId"):
        size = video.stat().st_size
        session = entry.get("uploadSession")
        offset = 0
        if session:
            offset, done = yt.upload_status(session, size)
            if done:
                entry["videoId"] = done["id"]
            elif offset < 0:
                session = None
        if not entry.get("videoId"):
            if not session:
                session = yt.start_upload(meta, size)
                entry["uploadSession"] = session  # resumable for ~1 week if we crash mid-upload
                save()
            res = yt.upload_file(session, video, offset=max(0, offset))
            entry["videoId"] = res["id"]
            entry["uploadStatus"] = (res.get("status") or {}).get("uploadStatus")
        entry.pop("uploadSession", None)
        entry["uploadedAt"] = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())
        save()
    vid = entry["videoId"]
    if not entry.get("thumbnail") and thumb.exists():
        yt.set_thumbnail(vid, thumb)
        entry["thumbnail"] = True
        save()
    if not entry.get("captionId") and vtt.exists():
        entry["captionId"] = yt.insert_captions(vid, vtt, meta["captions"]["language"], meta["captions"]["name"])
        save()
    if not entry.get("playlistItemId"):
        title = meta["playlist"]["title"]
        pid = book["playlists"].get(title) or yt.find_playlist(title)
        if not pid:
            pid = yt.create_playlist(title, meta["playlist"]["description"], meta["status"]["privacyStatus"])
        book["playlists"][title] = pid
        entry["playlistItemId"] = yt.add_to_playlist(pid, vid)
        save()
    # Patch for the coordinator (never written into the course JSON directly).
    patch_file = patch_file or (cfg.work_dir / "lecture-src-patch.json")
    patch = read_json(patch_file, {"lectures": {}}) or {"lectures": {}}
    patch["lectures"][key] = {"src": f"https://www.youtube.com/watch?v={vid}", "publishedAt": entry["uploadedAt"][:10],
                              "durationSeconds": meta.get("durationSeconds")}
    write_json(patch_file, patch)
    return {"key": key, "videoId": vid, "url": f"https://www.youtube.com/watch?v={vid}", "quotaUsed": yt.quota_used}
