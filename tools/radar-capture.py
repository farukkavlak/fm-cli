#!/usr/bin/env python3
"""
Capture the live /radar frames while a match is played.

The plugin is already running inside the game; this only polls its HTTP bridge.
One file per changed frame plus a `states.jsonl` timeline, which is what
`radar-styles.py` and `radar-gif.py` consume.

    python3 tools/radar-capture.py --out /tmp/radar-frames --minutes 50

Stop early with Ctrl-C; a match that has gone quiet for `--quiet` seconds also
ends the capture (the simulation empties between highlights and after the
whistle).
"""

from __future__ import annotations

import argparse
import hashlib
import json
import time
import urllib.request
from pathlib import Path


def get(base: str, path: str, timeout: float = 4.0):
    with urllib.request.urlopen(base + path, timeout=timeout) as response:
        return json.loads(response.read().decode("utf-8"))


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", type=Path, default=Path("radar-frames"))
    parser.add_argument("--base", default="http://127.0.0.1:7777")
    parser.add_argument("--minutes", type=float, default=50.0)
    parser.add_argument("--every", type=float, default=1.0, help="seconds between reads")
    parser.add_argument("--quiet", type=float, default=90.0, help="stop after this many empty seconds")
    args = parser.parse_args()

    args.out.mkdir(parents=True, exist_ok=True)
    deadline = time.time() + args.minutes * 60
    state_at = 0.0
    saved = 0
    seen = 0
    last_good = 0.0
    last_hash = None

    print(f"capturing to {args.out}; Ctrl-C stops", flush=True)
    while time.time() < deadline:
        now = time.time()
        try:
            radar = get(args.base, "/radar")
        except Exception:
            radar = None

        if now - state_at > 5:
            state_at = now
            try:
                state = get(args.base, "/state")
            except Exception:
                state = None
            if state:
                with (args.out / "states.jsonl").open("a") as f:
                    f.write(json.dumps({"t": round(now, 2), "state": state}) + "\n")

        if radar and radar.get("players"):
            seen += 1
            last_good = now
            digest = hashlib.sha1(json.dumps(radar, sort_keys=True).encode()).hexdigest()
            if digest != last_hash:
                last_hash = digest
                saved += 1
                record = {"t": round(now, 2), "radar": radar}
                try:
                    record["state"] = get(args.base, "/state", timeout=2)
                except Exception:
                    pass
                (args.out / f"frame-{saved:05d}.json").write_text(json.dumps(record))
                (args.out / "latest.json").write_text(json.dumps(record))
                if saved % 25 == 0:
                    print(f"  {saved} frames, {radar.get('count', 0)} players", flush=True)
        elif seen and now - last_good > args.quiet:
            break

        time.sleep(args.every)

    (args.out / "done.txt").write_text(
        f"saved={saved} seen={seen} at={time.strftime('%Y-%m-%d %H:%M:%S')}\n")
    print(f"done: {saved} frames, {seen} reads", flush=True)


if __name__ == "__main__":
    main()
