#!/usr/bin/env python3
"""Play `fm match --play` in a pty and keep every redraw of the live panel.

The panel clears the screen before each redraw, so the output is cut there: one
`frame-NNNNN.txt` per redraw (ANSI kept), a `frames.jsonl` index (time, keys
pressed so far) and the whole `raw.log`. `live-gif.py` turns the frames into GIFs.

Keys are sent at given seconds, e.g. `--key 120:r --key 200:a`, and whatever is appended to
`OUT/keys.in` while it runs is typed too (`printf '\r' >> OUT/keys.in` answers a prompt).

    python3 tools/live-capture.py --out /tmp/live --cols 120 --rows 40 --key 150:r
"""
from __future__ import annotations

import argparse
import fcntl
import json
import os
import pty
import select
import struct
import termios
import time
from pathlib import Path

CLEAR = b"\x1b[H\x1b[2J"


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", type=Path, required=True)
    ap.add_argument("--cols", type=int, default=120)
    ap.add_argument("--rows", type=int, default=40)
    ap.add_argument("--key", action="append", default=[], help="SECONDS:KEY")
    ap.add_argument("--minutes", type=float, default=20)
    ap.add_argument("--truecolor", action=argparse.BooleanOptionalAction, default=True)
    ap.add_argument("cmd", nargs="*", default=["fm", "match", "--play"])
    args = ap.parse_args()

    keys = sorted((float(k.split(":", 1)[0]), k.split(":", 1)[1]) for k in args.key)
    args.out.mkdir(parents=True, exist_ok=True)
    for old in args.out.glob("frame-*.txt"):
        old.unlink()

    env = dict(os.environ, TERM="xterm-256color")
    if args.truecolor:
        env["COLORTERM"] = "truecolor"
    else:
        env.pop("COLORTERM", None)
        env["TERM_PROGRAM"] = "Apple_Terminal"

    pid, fd = pty.fork()
    if pid == 0:
        os.execvpe(args.cmd[0], args.cmd, env)
    fcntl.ioctl(fd, termios.TIOCSWINSZ, struct.pack("HHHH", args.rows, args.cols, 0, 0))

    start = time.time()
    raw = open(args.out / "raw.log", "wb", buffering=0)
    index = open(args.out / "frames.jsonl", "w", buffering=1)
    pending = b""
    count = 0
    sent: list[str] = []
    inbox = args.out / "keys.in"
    inbox.write_bytes(b"")
    typed = 0

    def flush(chunk: bytes) -> None:
        nonlocal count
        if not chunk.strip():
            return
        count += 1
        (args.out / f"frame-{count:05d}.txt").write_bytes(chunk)
        index.write(json.dumps({"n": count, "t": round(time.time() - start, 2), "keys": "".join(sent)}) + "\n")

    while time.time() - start < args.minutes * 60:
        while keys and time.time() - start >= keys[0][0]:
            key = keys.pop(0)[1]
            os.write(fd, key.encode())
            sent.append(key)
            print(f"{time.time() - start:6.1f}s  key {key!r}", flush=True)
        extra = inbox.read_bytes()[typed:]
        if extra:
            typed += len(extra)
            os.write(fd, extra)
            sent.append(extra.decode(errors="replace"))
            print(f"{time.time() - start:6.1f}s  typed {extra!r}", flush=True)
        ready, _, _ = select.select([fd], [], [], 0.2)
        if not ready:
            continue
        try:
            data = os.read(fd, 65536)
        except OSError:
            break
        if not data:
            break
        raw.write(data)
        pending += data
        while True:
            cut = pending.find(CLEAR, 1)
            if cut < 0:
                break
            flush(pending[:cut])
            pending = pending[cut:]
    flush(pending)
    raw.close()
    index.close()
    try:
        os.kill(pid, 15)
    except ProcessLookupError:
        pass
    os.waitpid(pid, 0)
    print(f"{count} frames in {time.time() - start:.0f}s -> {args.out}")


if __name__ == "__main__":
    main()
