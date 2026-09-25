#!/usr/bin/env python3
"""Export the academy's video-lecture scripts for production (ElevenLabs narration + animated branded slides, published
on YouTube). See docs/lectures/README.md for the production workflow.

For every v2 course pack (a pack that declares "lastReviewed") in the catalog it writes:

  docs/lectures/<course-slug>.md   human-readable production script: per lesson the lecture title, target minutes,
                                   estimated runtime, pronunciations and the scene table (narration / on-screen /
                                   visual / seconds), plus the narration per scene ready to paste into ElevenLabs
  docs/lectures/manifest.jsonl     one JSON line per lecture (course, lesson, title, scenes, word count, estimated
                                   seconds, voice notes, pronunciations, production status)

Options:
  --catalog DIR     course packs (default backend/src/OptimizeAll.Api/Modules/Learning/Catalog)
  --out DIR         output folder (default docs/lectures)
  --only SLUG ...   only these courses
  --vtt             also write planned WebVTT captions per lecture to <out>/captions/<course>/<lesson>.vtt (timed from
                    the scene plan; re-time them against the final audio before upload)
  --include-incomplete
                    also export v2 packs where some lessons still lack a lecture (by default such packs are skipped
                    with a warning: they are still being written)
  --check           exit 1 when any exported lecture is outside the contract's ranges (scenes 5-16, 40-260 words per
                    scene, 600-1800 words per lecture)

Only the Python standard library is used. Run from the repository root:
  python3 scripts/export-lecture-scripts.py
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_CATALOG = ROOT / 'backend' / 'src' / 'OptimizeAll.Api' / 'Modules' / 'Learning' / 'Catalog'
DEFAULT_OUT = ROOT / 'docs' / 'lectures'

WORDS_PER_MINUTE = 140          # the contract's speaking rate (targetMinutes ≈ words / 140)
WORDS_PER_SECOND = 2.3          # scene seconds ≈ words / 2.3
YT_ID = re.compile(r'^[A-Za-z0-9_-]{11}$')

CATEGORY_TONE = {
    'ai': 'precise and technical, calm confidence; spell out code identifiers when read aloud',
    'marketing': 'energetic but grounded, practical examples',
    'seo': 'clear and methodical, explain jargon the first time',
    'sales': 'warm, conversational, story-led',
    'business': 'measured, executive-friendly',
    'design': 'visual and descriptive, relaxed pace',
    'data': 'clear and exact with numbers; say units',
    'platform': 'friendly, step-by-step',
}


def words(text: str | None) -> int:
    return len((text or '').split())


def md_cell(text: str | None) -> str:
    """A Markdown table cell: pipes escaped, line breaks kept as <br>."""
    return (text or '').replace('\\', '\\\\').replace('|', '\\|').replace('\r', '').replace('\n', '<br>')


def youtube_id(src: str | None) -> str | None:
    from urllib.parse import parse_qs, urlparse
    if not src:
        return None
    u = urlparse(src)
    if u.scheme != 'https':
        return None
    host, path = (u.hostname or '').lower(), u.path.rstrip('/')
    vid = None
    if host in ('www.youtube.com', 'youtube.com', 'm.youtube.com') and path == '/watch':
        vid = (parse_qs(u.query).get('v') or [None])[0]
    elif host == 'youtu.be' and path.count('/') == 1:
        vid = path[1:]
    elif host in ('www.youtube-nocookie.com', 'youtube-nocookie.com') and path.startswith('/embed/') and path.count('/') == 2:
        vid = path[len('/embed/'):]
    return vid if vid and YT_ID.match(vid) else None


def clock(seconds: float) -> str:
    s = int(round(seconds))
    h, s = divmod(s, 3600)
    return f'{h}:{s // 60:02d}:{s % 60:02d}' if h else f'{s // 60}:{s % 60:02d}'


def vtt_time(seconds: float) -> str:
    ms = int(round(seconds * 1000))
    h, ms = divmod(ms, 3_600_000)
    m, ms = divmod(ms, 60_000)
    s, ms = divmod(ms, 1000)
    return f'{h:02d}:{m:02d}:{s:02d}.{ms:03d}'


def chapter_title(on_screen: str | None) -> str:
    for line in (on_screen or '').split('\n'):
        line = line.strip().lstrip('•-* ').strip()
        if line:
            return line
    return ''


def voice_notes(pack: dict, lecture: dict) -> str:
    tone = CATEGORY_TONE.get(pack.get('category', ''), 'conversational expert')
    level = pack.get('level', 'beginner')
    notes = [f'Conversational expert, second person, ~{WORDS_PER_MINUTE} wpm; {tone}; audience level: {level}.',
             'Pause ~0.5 s between scenes; no music under narration (added in edit).']
    prons = lecture.get('pronunciations') or []
    if prons:
        notes.append('Pronounce: ' + '; '.join(f"{p['term']} = \"{p['say']}\"" for p in prons if p.get('term') and p.get('say')) + '.')
    return ' '.join(notes)


def sentence_cues(text: str, start: float, length: float) -> list[tuple[float, float, str]]:
    """Split narration into ≤ 2-line caption cues, timed proportionally to word count within the scene."""
    sentences = [s.strip() for s in re.split(r'(?<=[.!?])\s+', text.strip()) if s.strip()]
    chunks: list[str] = []
    for s in sentences:
        ws = s.split()
        while len(ws) > 16:
            chunks.append(' '.join(ws[:12]))
            ws = ws[12:]
        if ws:
            chunks.append(' '.join(ws))
    total = sum(len(c.split()) for c in chunks) or 1
    cues, t = [], start
    for c in chunks:
        d = length * len(c.split()) / total
        cues.append((t, t + d, c))
        t += d
    return cues


def lecture_problems(lecture: dict) -> list[str]:
    scenes = lecture.get('scenes') or []
    out = []
    if not 5 <= len(scenes) <= 16:
        out.append(f'{len(scenes)} scenes (5-16)')
    for i, s in enumerate(scenes):
        w = words(s.get('narration'))
        if not 40 <= w <= 260:
            out.append(f'scene {i + 1}: {w} words (40-260)')
    total = sum(words(s.get('narration')) for s in scenes)
    if not 600 <= total <= 1800:
        out.append(f'{total} narration words (600-1800)')
    return out


def export_pack(pack: dict, out: Path, write_vtt: bool) -> tuple[list[dict], list[str]]:
    slug = pack['slug']
    lines: list[str] = []
    manifest: list[dict] = []
    problems: list[str] = []
    lessons = [(m, l) for m in pack.get('modules') or [] for l in m.get('lessons') or []]
    lectures = [(m, l) for m, l in lessons if l.get('lecture')]
    total_words = sum(words(s.get('narration')) for _, l in lectures for s in l['lecture'].get('scenes') or [])
    total_seconds = sum(s.get('seconds', 0) for _, l in lectures for s in l['lecture'].get('scenes') or [])

    lines += [
        f"# {pack['title']} — lecture production script",
        '',
        f"Course `{slug}` · pack v{pack.get('version')} · reviewed {pack.get('lastReviewed', '—')} · level {pack.get('level')} · "
        f"category {pack.get('category')}",
        '',
        f'{len(lectures)} lectures · {total_words:,} narration words · planned runtime {clock(total_seconds)} '
        f'(≈ {round(total_words / WORDS_PER_MINUTE)} min at {WORDS_PER_MINUTE} wpm)',
        '',
        '> Generated by `scripts/export-lecture-scripts.py` from the course pack — edit the pack, not this file. '
        'Production workflow: [README](README.md).',
        '',
        '## Voice direction',
        '',
        voice_notes(pack, {}),
        '',
        '## Lectures',
        '',
    ]
    for i, (m, l) in enumerate(lectures, 1):
        lines.append(f"{i}. [{l['lecture'].get('title') or l['title']}](#{i}-{re.sub(r'[^a-z0-9]+', '-', (l['lecture'].get('title') or l['title']).lower()).strip('-')})")
    lines.append('')

    for i, (m, l) in enumerate(lectures, 1):
        lec = l['lecture']
        scenes = lec.get('scenes') or []
        title = lec.get('title') or l['title']
        w = sum(words(s.get('narration')) for s in scenes)
        planned = sum(s.get('seconds', 0) for s in scenes)
        yt = youtube_id(lec.get('src'))
        status = 'published (YouTube)' if yt else ('published (file)' if lec.get('src') else 'to produce')
        for p in lecture_problems(lec):
            problems.append(f"{slug}/{l['slug']}: {p}")
        lines += [
            f'### {i}. {title}',
            '',
            f"Lesson `{l['slug']}` · module “{m['title']}” · target {lec.get('targetMinutes')} min · {len(scenes)} scenes · "
            f'{w} words · planned {clock(planned)} (words ≈ {clock(w / WORDS_PER_SECOND)}) · status: **{status}**'
            + (f' · https://youtu.be/{yt}' if yt else ''),
            '',
        ]
        prons = lec.get('pronunciations') or []
        if prons:
            lines += ['| Term | Say |', '|---|---|']
            lines += [f"| {md_cell(p.get('term'))} | {md_cell(p.get('say'))} |" for p in prons]
            lines.append('')
        lines += ['| # | Time | Sec | Narration | On screen | Visual |', '|---|---|---|---|---|---|']
        t = 0
        for n, s in enumerate(scenes, 1):
            lines.append(f"| {n} | {clock(t)} | {s.get('seconds')} | {md_cell(s.get('narration'))} | {md_cell(s.get('onScreen'))} | "
                         f"{md_cell(s.get('visual'))} |")
            t += s.get('seconds', 0)
        lines += ['', '<details><summary>Narration per scene (paste into ElevenLabs)</summary>', '']
        for n, s in enumerate(scenes, 1):
            lines += [f'Scene {n} — {chapter_title(s.get("onScreen"))}', '', '```text', (s.get('narration') or '').strip(), '```', '']
        lines += ['</details>', '']

        manifest.append({
            'course': slug,
            'courseTitle': pack['title'],
            'packVersion': pack.get('version'),
            'lastReviewed': pack.get('lastReviewed'),
            'module': m['slug'],
            'lesson': l['slug'],
            'lessonTitle': l['title'],
            'position': i,
            'title': title,
            'targetMinutes': lec.get('targetMinutes'),
            'scenes': len(scenes),
            'chapters': [chapter_title(s.get('onScreen')) for s in scenes],
            'words': w,
            'estimatedSeconds': planned,
            'estimatedSecondsFromWords': round(w / WORDS_PER_SECOND),
            'voiceNotes': voice_notes(pack, lec),
            'pronunciations': prons,
            'status': status,
            'youtubeId': yt,
            'publishedAt': lec.get('publishedAt'),
        })

        if write_vtt:
            cues, t = [], 0.0
            for s in scenes:
                cues += sentence_cues(s.get('narration') or '', t, float(s.get('seconds', 0)))
                t += s.get('seconds', 0)
            vtt = ['WEBVTT', '', f'NOTE Planned timing for "{title}" ({slug}/{l["slug"]}); re-time against the final audio.', '']
            for k, (a, b, text) in enumerate(cues, 1):
                vtt += [str(k), f'{vtt_time(a)} --> {vtt_time(b)}', text, '']
            target = out / 'captions' / slug / f"{l['slug']}.vtt"
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text('\n'.join(vtt), encoding='utf-8')

    (out / f'{slug}.md').write_text('\n'.join(lines), encoding='utf-8')
    return manifest, problems


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--catalog', type=Path, default=DEFAULT_CATALOG)
    ap.add_argument('--out', type=Path, default=DEFAULT_OUT)
    ap.add_argument('--only', nargs='*', default=None)
    ap.add_argument('--vtt', action='store_true')
    ap.add_argument('--include-incomplete', action='store_true')
    ap.add_argument('--check', action='store_true')
    args = ap.parse_args(argv)

    args.out.mkdir(parents=True, exist_ok=True)
    all_manifest: list[dict] = []
    all_problems: list[str] = []
    exported = skipped = 0
    for path in sorted(args.catalog.glob('*.json')):
        try:
            pack = json.loads(path.read_text(encoding='utf-8'))
        except (OSError, json.JSONDecodeError) as e:
            print(f'skip {path.name}: {e}', file=sys.stderr)
            continue
        if args.only and pack.get('slug') not in args.only:
            continue
        if not pack.get('lastReviewed'):
            continue  # v1 pack: no lectures yet
        lessons = [l for m in pack.get('modules') or [] for l in m.get('lessons') or []]
        missing = [l['slug'] for l in lessons if not l.get('lecture')]
        if missing and not args.include_incomplete:
            print(f"skip {pack['slug']}: {len(missing)} of {len(lessons)} lessons have no lecture yet (in progress)", file=sys.stderr)
            skipped += 1
            continue
        manifest, problems = export_pack(pack, args.out, args.vtt)
        all_manifest += manifest
        all_problems += problems
        exported += 1

    with (args.out / 'manifest.jsonl').open('w', encoding='utf-8') as f:
        for row in all_manifest:
            f.write(json.dumps(row, ensure_ascii=False) + '\n')
    minutes = round(sum(r['words'] for r in all_manifest) / WORDS_PER_MINUTE)
    print(f'exported {exported} course(s), {len(all_manifest)} lecture(s), ≈ {minutes} min of narration → {args.out}'
          + (f'; skipped {skipped} in-progress pack(s)' if skipped else ''))
    for p in all_problems:
        print(f'warning: {p}', file=sys.stderr)
    return 1 if args.check and all_problems else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
