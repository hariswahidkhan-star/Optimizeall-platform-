# Academy video lectures — production workflow (ElevenLabs → YouTube)

Every lesson of a v2 course pack carries a production-ready **lecture script** (`lesson.lecture`: 5–16 scenes, each
with narration, on-screen text, visual direction and planned seconds). This folder holds the scripts exported for the
production team and this workflow. Lectures are **narrated with ElevenLabs over animated branded slides** (no avatars,
no talking heads) and **published on YouTube**, then embedded in the lesson with the privacy-enhanced player.

Until a lecture is published, the lesson page shows “Lecture — coming soon” with the chapters and the full transcript,
so learners and search engines already get the content.

## 1. Export the scripts

```bash
python3 scripts/export-lecture-scripts.py            # all finished v2 packs → docs/lectures/
python3 scripts/export-lecture-scripts.py --only ai-agents-engineering --vtt   # one course + planned captions
python3 scripts/export-lecture-scripts.py --check    # exit 1 if a lecture is outside the contract's ranges
```

Output (generated — edit the course pack, never these files):

| File | What it is |
|---|---|
| `<course-slug>.md` | The production script: per lecture the title, target minutes, planned runtime, pronunciations, the scene table (time, seconds, narration, on-screen text, visual direction) and the narration per scene ready to paste |
| `manifest.jsonl` | One line per lecture: course, lesson, title, chapters, scenes, word count, planned seconds, voice notes, pronunciations, status (`to produce` / `published (YouTube)`), YouTube id |
| `captions/<course>/<lesson>.vtt` | With `--vtt`: WebVTT captions timed from the scene plan (re-time them against the final audio) |

Packs still being written (some lessons without a lecture) are skipped with a warning; `--include-incomplete` exports
them anyway for a preview.

## 2. Voice (ElevenLabs)

1. **Choose one voice per course family** and keep it for every lecture of the course (consistency matters more than
   variety). Use a clear, warm, neutral-accent voice from the ElevenLabs voice library, or design one with Voice Design
   (“confident, friendly expert, mid-pace, neutral international English”). Never clone a real person’s voice without
   their written consent.
2. **Model and settings:** a current multilingual/high-quality TTS model; stability around 0.45–0.55, similarity around
   0.75, style low (0–0.2), speaker boost on. Keep the same settings for the whole course; record them in the course’s
   production sheet.
3. **Pronunciations:** add the lecture’s `pronunciations` (and the course’s recurring terms) to an ElevenLabs
   pronunciation dictionary for the project, e.g. `GA4 → “G A four”`, `SEO → “S E O”`, `n8n → “n eight n”`. The manifest’s
   `voiceNotes` summarises tone, pace and pronunciations per lecture.
4. **Generate narration per scene** (one audio file per scene, named `<lesson>-s01.mp3`, `-s02` …): paste the scene’s
   narration from the “Narration per scene” block. Per-scene files make fixes cheap and keep chapter boundaries exact.
5. Listen through once at 1×; regenerate any scene with a mispronunciation, odd emphasis or artefact. Target ≈ 140 words
   per minute; the planned seconds per scene are a guide (± 15%).

## 3. Visuals (animated branded slides)

For each scene, build the slide from **On screen** (title line + up to four “• ” bullets) and animate it following
**Visual** (diagram described, screen-recording steps, code on screen, b-roll prompt):

- Brand template: Optimize All colours, Inter Tight titles, lower-third with the course name, safe margins for YouTube.
- Screen recordings: record the real product being taught (logos only as they appear in the product UI); blur personal
  data, keys and tokens.
- AI video/b-roll (e.g. ElevenLabs video, other generators): only when the Visual asks for it; no real people’s likeness,
  no brand logos, no text baked into generated footage (put text on the slide layer).
- Code on screen: large monospace, syntax-highlighted, one idea per scene; never show real secrets.

## 4. Assemble

1. In the editor, place scene audio back to back with ~0.3–0.5 s gaps; lay each scene’s slide/visual over its audio.
2. Chapter boundaries = scene boundaries. Note each scene’s start time (the chapter list on the page and the YouTube
   description chapters use these).
3. Add subtle music bed at −28 to −32 LUFS under the voice, loudness-normalise the master to about −14 LUFS integrated.
4. Export 1080p (or 4K) H.264, 16:9, 30 fps.

## 5. Captions

Start from the planned VTT (`--vtt`) or generate captions from the final audio (ElevenLabs Speech-to-Text or YouTube’s
auto-captions), then correct them against the narration text — it is the script, word for word. Upload the corrected
captions to YouTube. Optionally host the VTT too (upload in the Learning admin) and set `lecture.captions`.

## 6. Publish on YouTube

- Title: the lecture title (≤ 100 chars) + “| Optimize All Academy”.
- Description: one-paragraph summary, the lesson URL (`https://<site>/learn/<course>/<lesson>`), then **chapters**
  (`0:00 Why this matters` …, first chapter at 0:00, from the scene start times), then the course link.
- Visibility: Public or Unlisted (both embed); category Education; not “made for kids”; add the course playlist.
- Thumbnail: the branded title card (1280×720). The lesson page uses YouTube’s `maxresdefault`/`hqdefault` thumbnail
  unless `lecture.poster` is set.
- Allow embedding (default).

## 7. Attach the video to the lesson

Either in the pack (then bump the pack’s `version` so the catalog upserts it):

```jsonc
"lecture": {
  "targetMinutes": 8,
  "scenes": [ /* unchanged */ ],
  "src": "https://youtu.be/dQw4w9WgXcQ",   // or https://www.youtube.com/watch?v=ID / https://www.youtube-nocookie.com/embed/ID
  "publishedAt": "2026-10-02",              // VideoObject uploadDate
  "captions": null,                          // optional: a VTT we host (/api/v1/files/{id} or https)
  "poster": null                             // optional: our own thumbnail
}
```

or, for a live course, in **Admin → Learning → course → lesson media** (the “lesson video” endpoint sets the lecture’s
`src`, `poster`, `captions` and `publishedAt` as a new course version, optionally published).

The validator accepts only the three YouTube URL forms with an 11-character id (or an https/uploaded MP4 for
self-hosted fallbacks). Once set, the lesson page shows the click-to-play facade (no YouTube code until play), then the
privacy-enhanced `youtube-nocookie.com` player with chapter seek, playback speed and transcript highlighting; the
server-rendered page embeds the player and emits `VideoObject` JSON-LD (embedUrl, thumbnails, uploadDate, duration,
transcript, chapter clips), and the video sitemap lists the lesson with `player_loc` = the embed URL.

## Quality checklist per lecture

- [ ] Narration matches the script (no ad-libs that change facts); pronunciations correct.
- [ ] Every on-screen claim is in the script; no invented numbers, versions or prices.
- [ ] Screen recordings free of personal data and secrets.
- [ ] Chapters in the YouTube description match the scene start times.
- [ ] Captions corrected and uploaded; the lesson page plays, seeks by chapter and shows “Watch now”.
- [ ] Pack `version` bumped (or admin version published) after setting `src`.
