import unittest

from studio import captions, metadata, timeline as tl
from studio.config import Config
from studio.packs import find_lecture, split_caption_chunks
from studio.plan import build_plan

from tests.fixtures import PACK


class SentenceTimingTests(unittest.TestCase):
    def test_proportional_without_silences(self):
        sents = ["Short one.", "A much longer second sentence with many more characters in it."]
        t = tl.sentence_times(sents, 10.0, [])
        self.assertEqual(t[0][0], 0.0)
        self.assertEqual(t[-1][1], 10.0)
        self.assertLess(t[0][1] - t[0][0], t[1][1] - t[1][0])

    def test_snaps_to_nearby_pause_and_skips_leading_silence(self):
        sents = ["a" * 50 + ".", "b" * 50 + "."]
        silences = [(0.0, 0.4), (5.9, 6.4), (9.8, 10.0)]
        t = tl.sentence_times(sents, 10.0, silences)
        self.assertAlmostEqual(t[0][0], 0.4, places=2)  # speech starts after the leading silence
        self.assertAlmostEqual(t[0][1], 6.15, places=2)  # boundary moved to the real pause
        self.assertAlmostEqual(t[1][1], 9.8, places=2)

    def test_alignment_is_exact(self):
        text = "Hi there. Bye now."
        al = {"characters": list(text), "character_start_times_seconds": [i * 0.1 for i in range(len(text))],
              "character_end_times_seconds": [i * 0.1 + 0.1 for i in range(len(text))]}
        t = tl.sentence_times(["Hi there.", "Bye now."], 2.0, None, al)
        self.assertAlmostEqual(t[1][0], 1.0, places=2)

    def test_match_items_monotonic_and_fill(self):
        sents = ["First we explore the code.", "Then we plan carefully.", "Finally verify everything."]
        times = [(0, 3), (3, 6), (6, 9)]
        raw = tl.match_items(["Explore", "Plan", "Zebra", "Verify"], sents, times)
        self.assertIsNone(raw[2])
        self.assertLess(raw[0], raw[1])
        filled = tl.fill_reveals(raw, 0.5, 9.0)
        self.assertEqual(len(filled), 4)
        self.assertTrue(all(b - a >= 0.55 - 1e-9 for a, b in zip(filled, filled[1:])))

    def test_frames(self):
        self.assertEqual(tl.frames_for(1.0, 30), 30)
        self.assertEqual(tl.frames_for(1.01, 30), 31)


class CaptionTests(unittest.TestCase):
    def test_chunks_are_short_and_complete(self):
        s = "In this lecture you will learn a five-step loop that professional teams use to prevent exactly that: explore, plan, implement, verify, and review."
        chunks = split_caption_chunks(s)
        self.assertTrue(all(len(c) <= 84 for c in chunks))
        self.assertEqual(" ".join(chunks), s)

    def test_cues_proportional_and_non_overlapping(self):
        cues = tl.caption_cues(["Short.", "x " * 60 + "end."], [(0.0, 1.0), (1.2, 9.2)], offset=0.5)
        self.assertEqual(cues[0]["start"], 0.5)
        for a, b in zip(cues, cues[1:]):
            self.assertLessEqual(a["end"], b["start"] + 1e-9)
        self.assertAlmostEqual(cues[-1]["end"], 9.7, places=2)

    def test_vtt_format(self):
        vtt = captions.to_vtt([{"start": 3661.5, "end": 3663.25, "text": "Hello world, this is a caption line that wraps nicely."}])
        self.assertTrue(vtt.startswith("WEBVTT\n\n1\n01:01:01.500 --> 01:01:03.250\n"))
        lines = vtt.strip().split("\n")[4:]
        self.assertEqual(len(lines), 2)
        self.assertEqual(captions.ts(0.0004), "00:00:00.000")


def fake_timeline(plan, scene_len=20.0):
    scenes, t = [], 3.0
    for sc in plan["scenes"]:
        scenes.append({"id": sc["id"], "chapter": sc["chapter"], "start": t, "duration": scene_len,
                       "cues": [{"start": 0.5, "end": 2.0, "text": "Hi."}]})
        t += scene_len
    return {"scenes": scenes, "intro": {"duration": 3.0}, "outro": {"start": t, "duration": 6.0}, "totalSeconds": t + 6}


class MetadataTests(unittest.TestCase):
    def setUp(self):
        self.cfg = Config()
        self.plan = build_plan(find_lecture(PACK, "budget-lesson"), self.cfg)

    def test_chapters_rules(self):
        tlm = fake_timeline(self.plan)
        tlm["scenes"][2]["start"] = tlm["scenes"][3]["start"] - 4  # a 4-second chapter must be folded
        ch = metadata.chapters(tlm)
        self.assertEqual(ch[0][0], 0.0)
        self.assertGreaterEqual(len(ch), 3)
        ends = [c[0] for c in ch[1:]] + [tlm["outro"]["start"]]
        self.assertTrue(all(e - s >= 10 for (s, _), e in zip(ch, ends)))
        self.assertEqual(metadata.fmt_chapter(75.9), "1:15")
        self.assertEqual(metadata.fmt_chapter(3725), "1:02:05")

    def test_build(self):
        meta = metadata.build(self.cfg, self.plan, fake_timeline(self.plan), PACK, credits=123.4)
        sn = meta["snippet"]
        self.assertLessEqual(len(sn["title"]), 100)
        self.assertIn("\n0:00 Introduction\n", sn["description"])
        self.assertIn(self.plan["lessonUrl"], sn["description"])
        self.assertEqual(sn["categoryId"], "27")
        self.assertEqual(meta["status"]["privacyStatus"], "unlisted")
        self.assertFalse(meta["status"]["selfDeclaredMadeForKids"])
        self.assertEqual(meta["playlist"]["title"], PACK["title"])
        tag_chars = sum(len(t) + (2 if " " in t else 0) + 1 for t in sn["tags"])
        self.assertLessEqual(tag_chars, 500)

    def test_long_title_is_shortened(self):
        plan = dict(self.plan, lectureTitle="L" * 80, course=dict(self.plan["course"], title="Course: with a very long subtitle here"))
        self.assertLessEqual(len(metadata.youtube_title(plan)), 100)
        self.assertEqual(metadata.youtube_title(plan), "L" * 80 + " | Course")


if __name__ == "__main__":
    unittest.main()
