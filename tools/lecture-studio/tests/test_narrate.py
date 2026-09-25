import base64
import tempfile
import unittest
from pathlib import Path

from studio import narrate
from studio.audio import run_ffmpeg
from studio.config import Config
from studio.packs import find_lecture
from studio.plan import build_plan

from tests.fixtures import PACK


def tone_mp3(seconds=1.0) -> bytes:
    with tempfile.TemporaryDirectory() as d:
        p = Path(d) / "t.mp3"
        run_ffmpeg(["-y", "-f", "lavfi", "-i", f"sine=frequency=440:duration={seconds}", "-ac", "1", "-b:a", "64k", str(p)])
        return p.read_bytes()


class NarrateTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.cfg = Config(work_dir=Path(self.tmp.name))
        self.plan = build_plan(find_lecture(PACK, "budget-lesson"), self.cfg)

    def tearDown(self):
        self.tmp.cleanup()

    def test_requests_list_only_missing(self):
        req = narrate.requests_for_agent(self.cfg, self.plan)
        self.assertEqual(req["generations_count"], 1)
        self.assertEqual(len(req["missing"]), len(self.plan["scenes"]))
        self.assertEqual(req["missingCharacters"], self.plan["ttsCharacters"])

    def test_refuses_foreign_hosts(self):
        for url in ("http://storage.googleapis.com/x.mp3", "https://evil.example/x.mp3", "https://storage.googleapis.com.evil.io/x"):
            with self.assertRaises(narrate.NarrationError):
                narrate.ingest(self.cfg, self.plan, "s01", url, http_get=lambda u: (b"", None))

    def test_ingest_caches_and_never_downloads_twice(self):
        calls = []
        audio = tone_mp3(1.0)

        def get(u):
            calls.append(u)
            return audio, "audio/mpeg"

        url = "https://storage.googleapis.com/bucket/content.mp3?X-Goog-Signature=abc"
        r1 = narrate.ingest(self.cfg, self.plan, "s01", url, credits=12.5, http_get=get)
        self.assertFalse(r1["cached"])
        self.assertAlmostEqual(r1["seconds"], 1.0, delta=0.08)
        r2 = narrate.ingest(self.cfg, self.plan, "s01", url, http_get=get)
        self.assertTrue(r2["cached"])
        self.assertEqual(len(calls), 1)
        key = self.plan["scenes"][0]["ttsKey"]
        self.assertTrue((self.cfg.audio_cache / f"{key}.mp3").exists())
        self.assertEqual(narrate.ledger_summary(self.cfg)["credits"], 12.5)
        # the signed URL is not persisted in the cache metadata
        self.assertNotIn("Signature", (self.cfg.audio_cache / f"{key}.json").read_text())

    def test_api_backend_dry_run_and_request(self):
        self.assertEqual(len(narrate.synthesize_api(self.cfg, self.plan, dry_run=True)), len(self.plan["scenes"]))
        sent = []
        audio = base64.b64encode(tone_mp3(0.5)).decode()

        def post(url, headers, body):
            sent.append((url, headers, body))
            return {"audio_base64": audio, "alignment": {"characters": ["a"], "character_start_times_seconds": [0.0]}}, {"character-cost": "42"}

        out = narrate.synthesize_api(self.cfg, self.plan, api_key="k-test", http_post=post)
        self.assertEqual(len(out), len(self.plan["scenes"]))
        url, headers, body = sent[0]
        self.assertIn(f"/v1/text-to-speech/{self.plan['voice']['id']}/with-timestamps", url)
        self.assertEqual(body["model_id"], "eleven_multilingual_v2")
        self.assertEqual(headers["xi-api-key"], "k-test")
        # everything cached now -> no further requests
        self.assertEqual(narrate.synthesize_api(self.cfg, self.plan, api_key="k-test", http_post=post), [])


if __name__ == "__main__":
    unittest.main()
