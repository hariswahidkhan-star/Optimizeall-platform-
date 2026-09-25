import copy
import unittest

from studio.config import VOICE_DANIEL, VOICE_JACOB, VOICE_VANESSA, Config
from studio.packs import apply_pronunciations, extract_code_blocks, find_lecture, split_sentences
from studio.plan import build_plan, chapter_title, choose_template, parse_on_screen, tts_cache_key

from tests.fixtures import PACK


class PronunciationTests(unittest.TestCase):
    def test_whole_word_and_longest_first(self):
        pr = [{"term": "GA", "say": "gee ay"}, {"term": "GA4", "say": "G A four"}, {"term": "ROAS", "say": "roe-as"}]
        self.assertEqual(apply_pronunciations("Use GA4 and ROAS, not GA.", pr), "Use G A four and roe-as, not gee ay.")
        # no replacement inside other words
        self.assertEqual(apply_pronunciations("ROASTED coffee", pr), "ROASTED coffee")

    def test_terms_with_punctuation(self):
        self.assertEqual(apply_pronunciations("Write PLAN.md now", [{"term": "PLAN.md", "say": "plan dot M D"}]),
                         "Write plan dot M D now")


class ParseTests(unittest.TestCase):
    def test_on_screen(self):
        p = parse_on_screen("Recap\n• One\n• Two\nNext: do it")
        self.assertEqual(p, {"title": "Recap", "bullets": ["One", "Two"], "extras": [], "next": "do it"})

    def test_sentences(self):
        self.assertEqual(split_sentences("One. Two, e.g. three? Four!"), ["One.", "Two, e.g. three?", "Four!"])

    def test_code_blocks(self):
        blocks = extract_code_blocks(PACK["modules"][0]["lessons"][1]["body"])
        self.assertEqual(len(blocks), 1)
        self.assertEqual(blocks[0]["lang"], "python")
        self.assertEqual(blocks[0]["heading"], "Hands-on")

    def test_chapter_titles(self):
        self.assertEqual(chapter_title(parse_on_screen("Anything"), 0, 5), "Introduction")
        self.assertEqual(chapter_title(parse_on_screen("3. Implement in small steps"), 2, 5), "Implement in small steps")
        self.assertEqual(chapter_title(parse_on_screen("Example: X (illustrative)"), 2, 5), "Example: X")
        self.assertEqual(chapter_title(parse_on_screen("Recap"), 4, 5), "Recap & next step")


class TemplateTests(unittest.TestCase):
    def setUp(self):
        self.cfg = Config()
        self.ref = find_lecture(PACK, "budget-lesson")
        self.plan = build_plan(self.ref, self.cfg)

    def test_templates_are_deterministic(self):
        got = [s["template"] for s in self.plan["scenes"]]
        self.assertEqual(got, ["title", "compare", "flow", "code", "keyidea", "bullets", "recap"])
        self.assertEqual(got, [s["template"] for s in build_plan(self.ref, self.cfg)["scenes"]])

    def test_template_data(self):
        sc = {s["id"]: s for s in self.plan["scenes"]}
        self.assertEqual(sc["s02"]["data"]["left"]["label"], "Average")
        self.assertEqual(sc["s03"]["data"]["nodes"], ["Explore", "Plan", "Verify"])
        self.assertEqual(sc["s04"]["data"]["block"]["lang"], "python")
        self.assertEqual(sc["s05"]["data"]["statement"], "Equalize marginal returns")
        self.assertEqual(sc["s06"]["data"]["variant"], "pitfalls")
        self.assertEqual(sc["s06"]["data"]["tryItem"], "sketch one curve")
        self.assertEqual(sc["s07"]["data"]["next"], "Design one experiment")

    def test_code_block_used_once(self):
        blocks = extract_code_blocks(self.ref.lesson["body"])
        used = set()
        scene = {"narration": "python response print", "onScreen": "Code\n• a", "visual": "code editor"}
        t1, _ = choose_template(scene, parse_on_screen(scene["onScreen"]), 3, 7, blocks, used)
        t2, _ = choose_template(scene, parse_on_screen(scene["onScreen"]), 4, 7, blocks, used)
        self.assertEqual((t1, t2), ("code", "bullets"))

    def test_tts_text_and_cache_key(self):
        s1 = self.plan["scenes"][0]
        self.assertIn("roe-as", s1["tts"])
        self.assertNotIn("ROAS", s1["tts"])
        self.assertEqual(s1["ttsKey"], tts_cache_key(VOICE_VANESSA, "eleven_multilingual_v2", s1["tts"]))
        self.assertNotEqual(tts_cache_key(VOICE_JACOB, "eleven_multilingual_v2", s1["tts"]), s1["ttsKey"])
        self.assertEqual(self.plan["ttsCharacters"], sum(len(s["tts"]) for s in self.plan["scenes"]))

    def test_voice_by_category(self):
        cfg = Config()
        self.assertEqual(cfg.voice_for("ai"), VOICE_JACOB)
        self.assertEqual(cfg.voice_for("SEO"), VOICE_VANESSA)
        self.assertEqual(cfg.voice_for("business"), VOICE_DANIEL)
        pack = copy.deepcopy(PACK)
        pack["category"] = "sales"
        self.assertEqual(build_plan(find_lecture(pack, "budget-lesson"), cfg)["voice"]["id"], VOICE_DANIEL)

    def test_lesson_url_and_numbering(self):
        self.assertEqual(self.plan["lessonUrl"], "https://www.optimizeall.com/learn/demo-course/budget-lesson")
        self.assertEqual(self.plan["lesson"]["number"], 2)


if __name__ == "__main__":
    unittest.main()
