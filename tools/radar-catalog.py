#!/usr/bin/env python3
"""
Render the live-match visualization catalog from mock match data.

No FM, no game, no network: `mock_match.py` supplies the same frame shape as
the plugin's `/radar` + `/state`, and every style is drawn from that one frame
by the `catalog` package (a small 3D stage plus terminal emulation).  The point
is to compare styles side by side before any of them is ported to the CLI.

    python3 tools/radar-catalog.py --out docs/experiments/catalog-draft
    python3 tools/radar-catalog.py --only 03 11      # just those tiles
    python3 tools/radar-catalog.py --ansi --cols 100 # real terminal output, one .ansi per card
    python3 tools/radar-catalog.py --show 03         # print one card to this terminal
"""

from __future__ import annotations

import argparse
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from catalog import layout  # noqa: E402


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", type=Path, default=Path("docs/experiments/catalog-draft"))
    parser.add_argument("--only", nargs="*", help="tile numbers to render, e.g. 03 11")
    parser.add_argument("--ansi", action="store_true", help="write terminal output (.ansi) instead of PNGs")
    parser.add_argument("--show", nargs="*", help="print these cards to this terminal")
    parser.add_argument("--preview", action="store_true", help="write PNGs of what a terminal --cols wide shows")
    parser.add_argument("--cols", type=int, default=100, help="terminal width for --ansi / --show")
    parser.add_argument("--mode", default="sext", choices=["sext", "quad", "half", "image"],
                        help="the 3D styles as sextants (2x3), quadrants (2x2), half blocks (1x2) or an inline image")
    args = parser.parse_args()

    if args.preview:
        from catalog import ansi
        from catalog.common import load
        m, goal = load(goal=False), load(goal=True)
        out = args.out.parent / "catalog-terminal" if args.out.name == "catalog-draft" else args.out
        out.mkdir(parents=True, exist_ok=True)
        for card in layout.CARDS:
            if args.only and card.number not in args.only:
                continue
            # 08-13 are inline-image styles; a cell-grid preview cannot show them.
            if "KITTY" in card.needs or "SIXEL" in card.needs:
                continue
            ansi.preview(card.number, m, goal, args.cols, args.mode).save(
                out / f"{card.number}-{card.title.lower().replace(' ', '-')}.png", optimize=True)
        print(out, f"terminal previews at {args.cols} columns")
        return

    if args.show is not None or args.ansi:
        from catalog import ansi
        numbers = args.show or args.only or [c.number for c in layout.CARDS]
        frames = ansi.build(numbers, args.cols, args.mode)
        if args.show is not None:
            for n, text in frames.items():
                card = next(c for c in layout.CARDS if c.number == n)
                print(f"\n  {n}  {card.title}  ({card.needs})\n")
                print(text)
            return
        out = args.out.parent / "catalog-ansi" if args.out.name == "catalog-draft" else args.out
        out.mkdir(parents=True, exist_ok=True)
        for n, text in frames.items():
            card = next(c for c in layout.CARDS if c.number == n)
            suffix = f".{args.mode}" if n in ("05", "06", "07") and args.mode != "sext" else ""
            (out / f"{n}-{card.title.lower().replace(' ', '-')}{suffix}.ansi").write_text(text + "\n")
        # one line to check the font draws the block characters the 3D styles use
        (out / "00-glyph-test.txt").write_text("sextants: \U0001FB00\U0001FB01\U0001FB02\U0001FB0F\U0001FB3B█▌▐   quadrants: ▘▝▖▗▚▞▙▟\n")
        print(out, f"{len(frames)} cards at {args.cols} columns")
        return

    args.out.mkdir(parents=True, exist_ok=True)

    t = time.time()
    cards, tiles, sheet = layout.build(args.only)
    for card, img in zip(cards, tiles):
        img.save(args.out / f"{card.number}-{card.title.lower().replace(' ', '-')}.png", optimize=True)
    if not args.only:
        sheet.save(args.out / "catalog.png", optimize=True)
        print(args.out / "catalog.png", sheet.size, f"{time.time() - t:.1f}s")
    else:
        print(f"{len(tiles)} tiles in {time.time() - t:.1f}s")


if __name__ == "__main__":
    main()
