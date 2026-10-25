#!/usr/bin/env python3
"""
Turn a run of captured /radar frames into a short pixel-art GIF.

    python3 tools/radar-gif.py FRAME_DIR --from 226 --to 245 --out radar.gif

The frames are the plugin's real positions; the GIF is a presentation render of
the half-block style (each terminal cell is two vertical pixels).
"""

from __future__ import annotations

import argparse
import importlib.util
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("radar_styles", ROOT / "radar-styles.py")
styles = importlib.util.module_from_spec(spec)
sys.modules["radar_styles"] = styles
spec.loader.exec_module(styles)  # type: ignore[union-attr]

MONO = "/System/Library/Fonts/Menlo.ttc"
BG = (10, 14, 19)
TEXT = (226, 234, 242)
DIM = (132, 150, 170)
ACCENT = (86, 214, 162)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("frames", type=Path)
    parser.add_argument("--from", dest="start", type=int, required=True)
    parser.add_argument("--to", dest="end", type=int, required=True)
    parser.add_argument("--out", type=Path, default=Path("docs/experiments/radar-styles/radar-goal.gif"))
    parser.add_argument("--home", default="Venezia")
    parser.add_argument("--away", default="Empoli")
    parser.add_argument("--competition", default="Serie BKT")
    parser.add_argument("--width", type=int, default=120)
    parser.add_argument("--duration", type=int, default=380, help="milliseconds per frame")
    args = parser.parse_args()

    files = []
    for n in range(args.start, args.end + 1):
        path = args.frames / f"frame-{n:05d}.json"
        if path.exists():
            files.append(path)
    if not files:
        raise SystemExit("no frames found")

    images: list[Image.Image] = []
    previous_score = None
    for path in files:
        frame = styles.load_frame(path, trail=4)
        art = styles.render_pixel(frame, args.width, args.home, args.away)
        art = art.resize((art.width * 6, art.height * 6), Image.Resampling.NEAREST)

        header = 84
        img = Image.new("RGB", (art.width, art.height + header), BG)
        d = ImageDraw.Draw(img)
        f_big = ImageFont.truetype(MONO, 26)
        f_small = ImageFont.truetype(MONO, 16)
        f_tiny = ImageFont.truetype(MONO, 13)

        hs, as_ = frame.score
        d.text((18, 12), f"{args.home} {hs}-{as_} {args.away}", font=f_big, fill=TEXT)
        d.text((18, 48), f"{args.competition}  ·  {frame.minute}  ·  {frame.period}", font=f_small, fill=DIM)
        d.text((art.width - 210, 16), "CANLI RADAR", font=f_tiny, fill=ACCENT)
        d.text((art.width - 210, 36), "pixel / half-block", font=f_tiny, fill=DIM)
        commentary = frame.state.get("Commentary") or ""
        d.text((art.width - 210, 56), commentary[:34], font=f_tiny, fill=DIM)

        score = (hs, as_)
        if previous_score is not None and score != previous_score and hs > previous_score[0]:
            d.rectangle([0, header - 4, art.width, header - 1], fill=(255, 214, 80))
            d.text((art.width // 2 - 26, 10), "GOL", font=f_big, fill=(255, 226, 120))
        previous_score = score

        img.paste(art, (0, header))
        images.append(img.convert("P", palette=Image.Palette.ADAPTIVE, colors=128))

    args.out.parent.mkdir(parents=True, exist_ok=True)
    images[0].save(
        args.out,
        save_all=True,
        append_images=images[1:],
        duration=args.duration,
        loop=0,
        optimize=True,
        disposal=2,
    )
    print(args.out, len(images), "frames", args.out.stat().st_size, "bytes")


if __name__ == "__main__":
    main()
