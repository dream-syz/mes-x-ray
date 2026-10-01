#!/usr/bin/env python3
"""MiniMax H3 helper for the 2-minute MES X-Ray cut. Standard library only; Python 3.9+.

    python3 h3.py plan                       # print the timeline (clip, start, duration, mode, frames, narration)
    python3 h3.py srt                        # write narration.srt + narration.txt (for TTS / hard subtitles)
    python3 h3.py submit --dry-run           # build every request and print it, without calling the API
    python3 h3.py submit --only C00,C11      # submit a subset (default resolution 768P for iteration)
    python3 h3.py submit --resolution 2K     # final renders
    python3 h3.py submit --voice             # keep the「画外旁白」paragraph so H3 narrates (plan B); default strips it (plan A)
    python3 h3.py poll --download            # poll tasks in out/tasks.json and download finished clips to out/
    python3 h3.py concat                     # write out/concat.txt and print the ffmpeg command
    python3 h3.py pack                       # zip everything the generation machine needs into out/h3-pack.zip

`plan` and `submit` first validate the pack: frames exist, are 16:9 and pairwise identical in size, prompts are
within the 7000-character limit, durations are 4–15 s integers.

Environment:  MINIMAX_API_KEY (required for submit/poll),  MINIMAX_API_BASE (default https://api.minimax.cn).
API shape follows https://platform.minimaxi.com/docs/api-reference/video-generation-v2-create.md (checked 2026-10-01).
"""
from __future__ import annotations

import argparse
import base64
import json
import os
import re
import struct
import sys
import time
import urllib.error
import urllib.request
import zipfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
OUT = HERE / "out"
MANIFEST = HERE / "manifest.json"
NARRATION_RE = re.compile(r"^画外旁白[^：]*：\s*(.*?)\s*$", re.M)
QUOTE_RE = re.compile(r"「(.*)」\s*$", re.S)
PROMPT_LIMIT = 7000
SILENT_LINE = "无对白、无旁白、无字幕。"


def png_size(path: Path) -> tuple[int, int] | None:
    with path.open("rb") as f:
        head = f.read(24)
    if len(head) < 24 or head[:8] != b"\x89PNG\r\n\x1a\n" or head[12:16] != b"IHDR":
        return None
    w, h = struct.unpack(">II", head[16:24])
    return w, h


def check(data: dict) -> list[str]:
    problems: list[str] = []
    for c in data["clips"]:
        cid = c["id"]
        if not (4 <= int(c["duration"]) <= 15):
            problems.append(f"{cid}: duration {c['duration']} is outside 4–15 s")
        prompt = HERE / c["prompt"]
        if not prompt.exists():
            problems.append(f"{cid}: missing prompt {c['prompt']}")
        elif len(prompt.read_text(encoding="utf-8")) > PROMPT_LIMIT:
            problems.append(f"{cid}: prompt longer than {PROMPT_LIMIT} characters")
        sizes = []
        for key in ("first_frame", "last_frame"):
            rel = c.get(key)
            if not rel:
                continue
            path = HERE / rel
            if not path.exists():
                problems.append(f"{cid}: missing {key} {rel}")
                continue
            size = png_size(path)
            if size is None:
                problems.append(f"{cid}: {rel} is not a PNG")
                continue
            sizes.append(size)
            if abs(size[0] / size[1] - 16 / 9) > 0.01:
                problems.append(f"{cid}: {rel} is {size[0]}×{size[1]}, not 16:9")
        if len(sizes) == 2 and sizes[0] != sizes[1]:
            problems.append(f"{cid}: first and last frame differ in size {sizes[0]} vs {sizes[1]}")
    return problems


def load_manifest() -> dict:
    data = json.loads(MANIFEST.read_text(encoding="utf-8"))
    t = 0
    for clip in data["clips"]:
        clip["start"] = t
        t += int(clip["duration"])
    data["total"] = t
    return data


def prompt_text(clip: dict, voice: bool) -> str:
    text = (HERE / clip["prompt"]).read_text(encoding="utf-8").strip()
    if not voice and NARRATION_RE.search(text):
        # Plan A: the picture is generated without speech; narration is one TTS take laid over the concatenation.
        # Say so explicitly instead of leaving a hole the model might fill with a voice of its own.
        text = NARRATION_RE.sub(SILENT_LINE, text, count=1)
        text = re.sub(r"\n{3,}", "\n\n", text).strip()
    return text


def narration(clip: dict) -> str | None:
    text = (HERE / clip["prompt"]).read_text(encoding="utf-8")
    m = NARRATION_RE.search(text)
    if not m:
        return None
    q = QUOTE_RE.search(m.group(1))
    return (q.group(1) if q else m.group(1)).strip()


def tc(seconds: float) -> str:
    ms = int(round(seconds * 1000))
    h, rem = divmod(ms, 3_600_000)
    m, rem = divmod(rem, 60_000)
    s, ms = divmod(rem, 1000)
    return f"{h:02d}:{m:02d}:{s:02d},{ms:03d}"


def split_sentences(text: str) -> list[str]:
    # keep the closing punctuation with its sentence; never split inside「」
    parts = re.split(r"(?<=[。；！？])", text)
    out = [p.strip() for p in parts if p.strip()]
    return out or [text]


def report_problems(data: dict, fatal: bool) -> None:
    problems = check(data)
    for p in problems:
        print("WARNING " + p, file=sys.stderr)
    if problems and fatal:
        sys.exit(f"{len(problems)} problem(s) — fix the pack before submitting")


def cmd_plan(data: dict) -> None:
    report_problems(data, fatal=False)
    print(f"{'clip':5} {'start':>6} {'dur':>4} {'mode':12} {'first → last frame':40} narration")
    for c in data["clips"]:
        mode = "first+last" if c["last_frame"] else "first_frame"
        frames = f"{Path(c['first_frame']).name} → {Path(c['last_frame']).name if c['last_frame'] else '—'}"
        n = narration(c)
        chars = len(re.sub(r"[，。：；、！？—「」\s]", "", n)) if n else 0
        pace = f"{chars / c['duration']:.1f} 字/s" if n else "—"
        print(f"{c['id']:5} {tc(c['start'])[3:8]:>6} {c['duration']:>3}s {mode:12} {frames:40} {chars:>3} 字 {pace}")
    print(f"total {data['total']} s = {tc(data['total'])[3:8]}")


def cmd_srt(data: dict) -> None:
    cues: list[tuple[float, float, str]] = []
    script_lines: list[str] = []
    for c in data["clips"]:
        n = narration(c)
        if not n:
            continue
        script_lines.append(f"[{c['id']} {tc(c['start'])[3:8]}] {n}")
        sentences = split_sentences(n)
        window_start = c["start"] + 0.6
        window_end = c["start"] + c["duration"] - 0.2
        total_chars = sum(len(s) for s in sentences) or 1
        t = window_start
        for s in sentences:
            d = (window_end - window_start) * len(s) / total_chars
            cues.append((t, t + d - 0.05, s))
            t += d
    srt = "".join(f"{i}\n{tc(a)} --> {tc(b)}\n{text}\n\n" for i, (a, b, text) in enumerate(cues, 1))
    (HERE / "narration.srt").write_text(srt, encoding="utf-8")
    (HERE / "narration.txt").write_text("\n".join(script_lines) + "\n", encoding="utf-8")
    print(f"narration.srt ({len(cues)} cues), narration.txt ({len(script_lines)} clips)")


def data_uri(path: Path) -> str:
    mime = "image/png" if path.suffix.lower() == ".png" else "image/jpeg"
    return f"data:{mime};base64," + base64.b64encode(path.read_bytes()).decode("ascii")


def build_payload(data: dict, clip: dict, resolution: str, voice: bool) -> dict:
    content: list[dict] = [{"type": "text", "text": prompt_text(clip, voice)}]
    first = HERE / clip["first_frame"]
    content.append({"type": "image_url", "image_url": {"url": data_uri(first)}, "role": "first_frame"})
    if clip["last_frame"]:
        content.append({"type": "image_url", "image_url": {"url": data_uri(HERE / clip["last_frame"])}, "role": "last_frame"})
    # i2va: ratio is dictated by the images (16:9 frames) and the API treats it as adaptive, so it is not sent.
    return {"model": data["model"], "content": content, "resolution": resolution, "duration": int(clip["duration"])}


def api(method: str, path: str, body: dict | None = None) -> dict:
    key = os.environ.get("MINIMAX_API_KEY")
    if not key:
        sys.exit("MINIMAX_API_KEY is not set (keep the key on the generation machine only; never commit it)")
    base = os.environ.get("MINIMAX_API_BASE", "https://api.minimax.cn").rstrip("/")
    req = urllib.request.Request(base + path, method=method, headers={"Authorization": f"Bearer {key}", "Content-Type": "application/json"})
    payload = json.dumps(body).encode("utf-8") if body is not None else None
    try:
        with urllib.request.urlopen(req, payload, timeout=120) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        sys.exit(f"{method} {path} → HTTP {e.code}: {e.read().decode('utf-8', 'replace')}")


def cmd_submit(data: dict, only: set[str] | None, resolution: str, voice: bool, dry_run: bool) -> None:
    report_problems(data, fatal=True)
    OUT.mkdir(exist_ok=True)
    tasks_path = OUT / "tasks.json"
    tasks = json.loads(tasks_path.read_text(encoding="utf-8")) if tasks_path.exists() else {}
    for c in data["clips"]:
        if only and c["id"] not in only:
            continue
        payload = build_payload(data, c, resolution, voice)
        size_kb = len(json.dumps(payload)) / 1024
        if dry_run:
            shown = json.loads(json.dumps(payload))
            for item in shown["content"]:
                if item["type"] == "image_url":
                    item["image_url"]["url"] = item["image_url"]["url"][:40] + f"… ({len(item['image_url']['url']) // 1024} KB)"
            print(f"--- {c['id']} {c['title']} ({size_kb:.0f} KB request)")
            print(json.dumps(shown, ensure_ascii=False, indent=2))
            continue
        resp = api("POST", "/v2/video_generation", payload)
        task_id = resp.get("task_id")
        tasks[c["id"]] = {"task_id": task_id, "resolution": resolution, "voice": voice, "submitted_at": int(time.time()), "status": "queued"}
        tasks_path.write_text(json.dumps(tasks, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"{c['id']} → task {task_id}")


def cmd_poll(download: bool, interval: int) -> None:
    tasks_path = OUT / "tasks.json"
    if not tasks_path.exists():
        sys.exit("out/tasks.json not found — run submit first")
    tasks = json.loads(tasks_path.read_text(encoding="utf-8"))
    pending = {cid: t for cid, t in tasks.items() if t.get("status") not in ("succeeded", "failed", "cancelled")}
    while pending:
        for cid, t in list(pending.items()):
            task = api("GET", f"/v2/query/video_generation/{t['task_id']}").get("task", {})
            status = task.get("status", "unknown")
            t["status"] = status
            if status == "succeeded":
                t["url"] = task.get("content", {}).get("url")
                print(f"{cid}: succeeded → {t['url']}")
                if download and t["url"]:
                    target = OUT / f"{cid}.mp4"
                    urllib.request.urlretrieve(t["url"], target)
                    t["file"] = str(target.relative_to(HERE))
                    print(f"{cid}: saved {t['file']}")
                pending.pop(cid)
            elif status in ("failed", "cancelled"):
                t["error"] = task.get("error")
                print(f"{cid}: {status} {t['error'] or ''}")
                pending.pop(cid)
            else:
                print(f"{cid}: {status}")
        tasks_path.write_text(json.dumps(tasks, ensure_ascii=False, indent=2), encoding="utf-8")
        if pending:
            time.sleep(interval)


def cmd_concat(data: dict) -> None:
    OUT.mkdir(exist_ok=True)
    lines = []
    missing = []
    for c in data["clips"]:
        f = OUT / f"{c['id']}.mp4"
        (lines if f.exists() else missing).append(f"file '{f.name}'")
    (OUT / "concat.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"out/concat.txt: {len(lines)} clips" + (f", missing {len(missing)}: {', '.join(m.split()[1] for m in missing)}" if missing else ""))
    print("ffmpeg -f concat -safe 0 -i out/concat.txt -c copy out/" + data["output"])
    print("# if the clips differ in encoding parameters, re-encode instead of -c copy:  -c:v libx264 -crf 18 -pix_fmt yuv420p -c:a aac")
    print("# plan A narration:  ffmpeg -i out/" + data["output"] + " -i narration.wav -map 0:v -map 1:a -c:v copy -shortest out/final.mp4")


def cmd_pack(data: dict) -> None:
    """Everything the generation machine needs, with the relative layout preserved (unzip, then run from h3/)."""
    report_problems(data, fatal=True)
    OUT.mkdir(exist_ok=True)
    demo = HERE.parent
    files: set[Path] = {HERE / "manifest.json", HERE / "h3.py", demo / "h3-brief.md"}
    for name in ("narration.srt", "narration.txt"):
        if (HERE / name).exists():
            files.add(HERE / name)
    for c in data["clips"]:
        files.add(HERE / c["prompt"])
        for key in ("first_frame", "last_frame"):
            if c.get(key):
                files.add((HERE / c[key]).resolve())
    target = OUT / "h3-pack.zip"
    with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as z:
        for f in sorted(files):
            z.write(f, f.relative_to(demo))
    print(f"{target.relative_to(HERE)}: {len(files)} files, {target.stat().st_size // 1024} KB")


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = p.add_subparsers(dest="cmd", required=True)
    sub.add_parser("plan")
    sub.add_parser("srt")
    sub.add_parser("pack")
    s = sub.add_parser("submit")
    s.add_argument("--only", help="comma-separated clip ids, e.g. C00,C11")
    s.add_argument("--resolution", default="768P", choices=["768P", "2K"])
    s.add_argument("--voice", action="store_true", help="keep the narration paragraph (plan B)")
    s.add_argument("--dry-run", action="store_true")
    q = sub.add_parser("poll")
    q.add_argument("--download", action="store_true")
    q.add_argument("--interval", type=int, default=10)
    sub.add_parser("concat")
    args = p.parse_args()

    data = load_manifest()
    if args.cmd == "plan":
        cmd_plan(data)
    elif args.cmd == "srt":
        cmd_srt(data)
    elif args.cmd == "submit":
        only = {x.strip() for x in args.only.split(",")} if args.only else None
        cmd_submit(data, only, args.resolution, args.voice, args.dry_run)
    elif args.cmd == "poll":
        cmd_poll(args.download, args.interval)
    elif args.cmd == "concat":
        cmd_concat(data)
    elif args.cmd == "pack":
        cmd_pack(data)


if __name__ == "__main__":
    main()
