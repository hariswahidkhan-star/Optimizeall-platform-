import json
import os
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from studio import youtube
from studio.config import Config
from studio.youtube import Resp, YouTube, YouTubeError, upload_lecture_dir


class FakeHttp:
    """Scripted HTTP: a list of (matcher, response) consumed in order; records every request."""

    def __init__(self, script):
        self.script = list(script)
        self.calls = []

    def request(self, method, url, *, headers=None, params=None, data=None, timeout=300):
        self.calls.append({"method": method, "url": url, "headers": headers or {}, "params": params or {}, "data": data})
        if url == youtube.TOKEN_URL:
            return Resp(200, {}, json.dumps({"access_token": "ya29.test", "expires_in": 3600}).encode())
        if not self.script:
            raise AssertionError(f"unexpected request {method} {url}")
        want, resp = self.script.pop(0)
        assert want in f"{method} {url}", f"expected {want}, got {method} {url}"
        return resp


def j(status, body, headers=None):
    return Resp(status, headers or {}, json.dumps(body).encode())


def client(script):
    return YouTube(FakeHttp(script), client_id="cid", client_secret="csecret", refresh_token="rtok", sleep=lambda s: None)


META = {
    "key": "demo/lesson",
    "snippet": {"title": "T", "description": "D", "tags": ["a"], "categoryId": "27"},
    "status": {"privacyStatus": "unlisted", "selfDeclaredMadeForKids": False},
    "playlist": {"title": "Demo Course", "description": "PD"},
    "captions": {"language": "en", "name": "English"},
    "files": {"video": "video.mp4", "thumbnail": "poster.png", "captions": "captions.vtt"},
    "durationSeconds": 12.5,
}


class RequestBuildingTests(unittest.TestCase):
    def test_token_refresh_and_start_upload(self):
        yt = client([("POST https://www.googleapis.com/upload/youtube/v3/videos", Resp(200, {"location": "https://up/session"}, b""))])
        loc = yt.start_upload(META, 1234)
        self.assertEqual(loc, "https://up/session")
        tok, start = yt.http.calls
        self.assertEqual(tok["data"]["grant_type"], "refresh_token")
        self.assertEqual(start["params"], {"uploadType": "resumable", "part": "snippet,status", "notifySubscribers": "false"})
        self.assertEqual(start["headers"]["authorization"], "Bearer ya29.test")
        self.assertEqual(start["headers"]["x-upload-content-length"], "1234")
        self.assertEqual(start["headers"]["x-upload-content-type"], "video/mp4")
        body = json.loads(start["data"])
        self.assertEqual(body["status"]["privacyStatus"], "unlisted")
        self.assertNotIn("playlist", body)

    def test_chunked_upload_with_308_and_retry(self):
        with tempfile.TemporaryDirectory() as d:
            p = Path(d) / "v.mp4"
            p.write_bytes(b"x" * (youtube.CHUNK + 10))
            yt = client([
                ("PUT https://up/s", Resp(308, {"range": f"bytes=0-{youtube.CHUNK - 1}"}, b"")),
                ("PUT https://up/s", Resp(503, {}, b"{}")),  # transient: ask status, then resend
                ("PUT https://up/s", Resp(308, {"range": f"bytes=0-{youtube.CHUNK - 1}"}, b"")),
                ("PUT https://up/s", j(200, {"id": "VID123", "status": {"uploadStatus": "uploaded"}})),
            ])
            res = yt.upload_file("https://up/s", p)
            self.assertEqual(res["id"], "VID123")
            puts = [c for c in yt.http.calls if c["method"] == "PUT"]
            self.assertEqual(puts[0]["headers"]["content-range"], f"bytes 0-{youtube.CHUNK - 1}/{youtube.CHUNK + 10}")
            self.assertEqual(puts[2]["headers"]["content-range"], f"bytes */{youtube.CHUNK + 10}")
            self.assertEqual(puts[3]["headers"]["content-range"], f"bytes {youtube.CHUNK}-{youtube.CHUNK + 9}/{youtube.CHUNK + 10}")

    def test_quota_exceeded_is_not_retried(self):
        err = {"error": {"code": 403, "message": "quota", "errors": [{"reason": "quotaExceeded"}]}}
        yt = client([("POST https://www.googleapis.com/upload/youtube/v3/captions", j(403, err))])
        with tempfile.TemporaryDirectory() as d:
            p = Path(d) / "c.vtt"
            p.write_text("WEBVTT\n")
            with self.assertRaises(YouTubeError) as ctx:
                yt.insert_captions("VID", p, "en", "English")
        self.assertEqual(ctx.exception.reason, "quotaExceeded")
        self.assertEqual(len([c for c in yt.http.calls if "captions" in c["url"]]), 1)

    def test_rate_limit_is_retried(self):
        err = {"error": {"code": 403, "errors": [{"reason": "rateLimitExceeded"}]}}
        yt = client([("POST https://www.googleapis.com/youtube/v3/playlistItems", j(403, err)),
                     ("POST https://www.googleapis.com/youtube/v3/playlistItems", j(200, {"id": "PLI"}))])
        self.assertEqual(yt.add_to_playlist("PL", "VID"), "PLI")
        self.assertEqual(yt.quota_used, 50)

    def test_caption_multipart_body(self):
        body, ctype = YouTube.caption_body("VID", b"WEBVTT\n\n1\n00:00:00.000 --> 00:00:01.000\nHi\n", "en", "English", boundary="BND")
        self.assertEqual(ctype, "multipart/related; boundary=BND")
        text = body.decode()
        self.assertIn('"videoId": "VID"', text)
        self.assertIn("Content-Type: text/vtt", text)
        self.assertTrue(text.endswith("--BND--\r\n"))

    def test_missing_credentials(self):
        with mock.patch.dict(os.environ, {}, clear=True):
            yt = YouTube(FakeHttp([]))
            with self.assertRaises(YouTubeError) as ctx:
                yt.token()
        self.assertIn("YOUTUBE_REFRESH_TOKEN", str(ctx.exception))

    def test_secrets_are_redacted_from_errors(self):
        with mock.patch.dict(os.environ, {"YOUTUBE_CLIENT_SECRET": "s3cr3t"}):
            e = youtube._error(Resp(400, {}, json.dumps({"error": {"message": "bad client s3cr3t"}}).encode()), "x")
        self.assertNotIn("s3cr3t", str(e))


class LedgerTests(unittest.TestCase):
    def test_publish_is_idempotent_and_writes_patch(self):
        with tempfile.TemporaryDirectory() as d:
            d = Path(d)
            out = d / "out"
            out.mkdir()
            (out / "video.mp4").write_bytes(b"v" * 100)
            (out / "poster.png").write_bytes(b"\x89PNG")
            (out / "captions.vtt").write_text("WEBVTT\n")
            (out / "youtube.json").write_text(json.dumps(META))
            cfg = Config(work_dir=d / "work")
            yt = client([
                ("POST https://www.googleapis.com/upload/youtube/v3/videos", Resp(200, {"location": "https://up/s"}, b"")),
                ("PUT https://up/s", j(201, {"id": "VID9"})),
                ("POST https://www.googleapis.com/upload/youtube/v3/thumbnails/set", j(200, {})),
                ("POST https://www.googleapis.com/upload/youtube/v3/captions", j(200, {"id": "CAP1"})),
                ("GET https://www.googleapis.com/youtube/v3/playlists", j(200, {"items": [{"id": "PLX", "snippet": {"title": "Other"}}]})),
                ("POST https://www.googleapis.com/youtube/v3/playlists", j(200, {"id": "PL1"})),
                ("POST https://www.googleapis.com/youtube/v3/playlistItems", j(200, {"id": "PLI1"})),
            ])
            res = upload_lecture_dir(cfg, out, yt=yt)
            self.assertEqual(res["url"], "https://www.youtube.com/watch?v=VID9")
            self.assertEqual(res["quotaUsed"], 400 + 50 + 1 + 50 + 50)
            thumb = next(c for c in yt.http.calls if "thumbnails" in c["url"])
            self.assertEqual(thumb["params"], {"videoId": "VID9", "uploadType": "media"})
            self.assertEqual(thumb["headers"]["content-type"], "image/png")
            patch = json.loads((cfg.work_dir / "lecture-src-patch.json").read_text())
            self.assertEqual(patch["lectures"]["demo/lesson"]["src"], "https://www.youtube.com/watch?v=VID9")
            self.assertRegex(patch["lectures"]["demo/lesson"]["publishedAt"], r"^\d{4}-\d{2}-\d{2}$")
            ledger = (cfg.work_dir / "youtube-ledger.json").read_text()
            self.assertNotIn("ya29", ledger)  # no tokens persisted
            # second run: nothing left to do, zero API calls besides none
            yt2 = client([])
            res2 = upload_lecture_dir(cfg, out, yt=yt2)
            self.assertEqual(res2["videoId"], "VID9")
            self.assertEqual(yt2.http.calls, [])
            self.assertEqual(upload_lecture_dir(cfg, out, dry_run=True)["todo"], [])


if __name__ == "__main__":
    unittest.main()
