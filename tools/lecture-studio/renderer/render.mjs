#!/usr/bin/env node
// Deterministic frame renderer: steps the stage timeline frame by frame (window.__seek(t)), screenshots each
// distinct frame with headless Chromium and pipes the frames into ffmpeg (libx264). Identical consecutive
// frames (same animation-state key) reuse the previous screenshot, which makes static stretches almost free.
//
// Usage: node renderer/render.mjs job.json
import { spawn } from "node:child_process";
import { existsSync, readFileSync, renameSync, mkdirSync, unlinkSync } from "node:fs";
import { createRequire } from "node:module";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));

async function loadPlaywright(extraNodeModules) {
  if (!process.env.PLAYWRIGHT_BROWSERS_PATH && existsSync("/opt/pw-browsers")) {
    process.env.PLAYWRIGHT_BROWSERS_PATH = "/opt/pw-browsers";
  }
  try {
    return await import("playwright");
  } catch {
    const req = createRequire(path.join(extraNodeModules || "", "noop.js"));
    return req("playwright");
  }
}

function fontCss(fonts) {
  const face = (family, file, weights, style = "normal") =>
    file
      ? `@font-face{font-family:"${family}";src:url("${pathToFileURL(file).href}") format("woff2");font-weight:${weights};font-style:${style};font-display:block;}`
      : "";
  return [
    face("Inter Variable", fonts.inter, "100 900"),
    face("Inter Tight Variable", fonts.interTight, "100 900"),
    face("JetBrains Mono Variable", fonts.mono, "100 800"),
  ].join("\n");
}

function startEncoder(job, out) {
  const args = [
    "-hide_banner", "-loglevel", "error", "-y",
    "-f", "image2pipe", "-framerate", String(job.fps), "-c:v", job.format === "png" ? "png" : "mjpeg", "-i", "-",
    "-vf", "scale=out_color_matrix=bt709:out_range=tv,format=yuv420p",
    "-c:v", "libx264", "-preset", job.preset || "medium", "-tune", "animation", "-crf", String(job.crf ?? 18),
    "-g", String(job.fps * 2), "-bf", "2",
    "-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "bt709", "-color_range", "tv",
    "-r", String(job.fps), "-an", "-movflags", "+faststart", out,
  ];
  const p = spawn(job.ffmpeg, args, { stdio: ["pipe", "inherit", "inherit"] });
  p.stdin.on("error", () => {}); // a dying ffmpeg surfaces through the exit code below
  const done = new Promise((resolve, reject) => {
    p.on("error", reject);
    p.on("close", (code) => (code === 0 ? resolve() : reject(new Error(`ffmpeg exited ${code} for ${out}`))));
  });
  return { p, done };
}

function write(stream, buf) {
  if (stream.write(buf)) return Promise.resolve();
  return new Promise((resolve) => stream.once("drain", resolve));
}

async function renderItem(page, cdp, job, item, log) {
  await page.evaluate((sc) => window.__build(sc), item.scene);
  await page.evaluate(() => document.fonts.ready);
  // Raw CDP capture is ~25% faster than page.screenshot (no stability waits); the frame state is already final
  // because __seek() mutates styles synchronously before we capture.
  const shotParams = job.format === "png"
    ? { format: "png", optimizeForSpeed: true }
    : { format: "jpeg", quality: job.quality ?? 94, optimizeForSpeed: true };
  const capture = async () => Buffer.from((await cdp.send("Page.captureScreenshot", shotParams)).data, "base64");
  for (const s of item.stills || []) {
    await page.evaluate((t) => window.__seek(t), s.t);
    mkdirSync(path.dirname(s.out), { recursive: true });
    await page.screenshot({ path: s.out, type: "png" });
  }
  if (job.stillsOnly) return { frames: 0, shots: 0 };
  const tmp = item.out.replace(/\.mp4$/, ".part.mp4");
  mkdirSync(path.dirname(item.out), { recursive: true });
  const enc = startEncoder(job, tmp);
  let lastKey = null, lastBuf = null, shots = 0;
  const t0 = Date.now();
  for (let f = 0; f < item.frames; f++) {
    const t = f / job.fps;
    const key = await page.evaluate((tt) => window.__seek(tt), t);
    if (key !== lastKey || !lastBuf) {
      lastBuf = await capture();
      lastKey = key;
      shots++;
    }
    await write(enc.p.stdin, lastBuf);
  }
  enc.p.stdin.end();
  await enc.done;
  renameSync(tmp, item.out);
  const secs = (Date.now() - t0) / 1000;
  log(`${item.id}: ${item.frames} frames (${shots} unique) in ${secs.toFixed(1)}s`);
  return { frames: item.frames, shots, secs };
}

async function main() {
  const jobPath = process.argv[2];
  if (!jobPath) throw new Error("usage: render.mjs job.json");
  const job = JSON.parse(readFileSync(jobPath, "utf8"));
  const { chromium } = await loadPlaywright(job.nodeModules);
  const browser = await chromium.launch({
    args: ["--font-render-hinting=none", "--force-color-profile=srgb", "--disable-lcd-text", "--hide-scrollbars",
      "--disable-gpu", "--disable-gpu-compositing"],
  });
  const queue = [...job.items];
  const workers = Math.max(1, Math.min(job.workers || 2, queue.length));
  const log = (m) => process.stderr.write(`[render] ${m}\n`);
  const stats = [];
  const fontStyle = fontCss(job.fonts || {});
  const stageUrl = pathToFileURL(path.join(here, "stage.html")).href;
  async function worker() {
    const ctx = await browser.newContext({ viewport: { width: job.width, height: job.height }, deviceScaleFactor: 1 });
    const page = await ctx.newPage();
    const cdp = await ctx.newCDPSession(page);
    page.on("pageerror", (e) => log(`page error: ${e.message}`));
    await page.goto(stageUrl);
    await page.addStyleTag({ content: fontStyle });
    // Warm the fonts so the very first frame is already set in the right typeface.
    await page.evaluate(async () => {
      const probes = ["400 20px 'Inter Variable'", "700 20px 'Inter Tight Variable'", "500 20px 'JetBrains Mono Variable'"];
      await Promise.all(probes.map((f) => document.fonts.load(f)));
    });
    while (queue.length) {
      const item = queue.shift();
      try {
        stats.push({ id: item.id, ...(await renderItem(page, cdp, job, item, log)) });
      } catch (e) {
        try { unlinkSync(item.out.replace(/\.mp4$/, ".part.mp4")); } catch {}
        throw e;
      }
    }
    await ctx.close();
  }
  try {
    await Promise.all(Array.from({ length: workers }, worker));
  } finally {
    await browser.close();
  }
  process.stdout.write(JSON.stringify({ ok: true, stats }) + "\n");
}

main().catch((e) => {
  process.stderr.write(`[render] FAILED: ${e.stack || e}\n`);
  process.exit(1);
});
