"""Tiles and the catalog sheet."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Callable

from PIL import Image, ImageDraw

from . import styles
from .common import ACCENT, BG, BORDER, DIM, FAINT, PANEL, TEXT, Match, font, load, shade
from .styles import ART

TILE_W, TILE_H = 584, 530
ART_Y = 116

TAGS = {
    "LIVE": (64, 200, 128),
    "REPLAY": (246, 196, 72),
    "EFFECT": (240, 84, 84),
    "ANALYSIS": (84, 156, 246),
    "CONCEPT": (168, 132, 236),
}


@dataclass
class Card:
    number: str
    title: str
    subtitle: str
    tag: str
    needs: str  # what the terminal has to support
    render: Callable[[Match], Image.Image]
    moment: str  # "live" or "goal"
    minute: str
    caption: str


CARDS = [
    Card("01", "CLASSIC ASCII", "Clean, lightweight, timeless.", "LIVE", "ANY TERMINAL", styles.ascii_classic, "live", "67'", "Ødegaard to Saka..."),
    Card("02", "COLORED ASCII", "Same simplicity, more life.", "LIVE", "256 COLOURS", styles.ascii_colour, "live", "67'", "Martinelli drives forward!"),
    Card("03", "TRUECOLOR PIXEL", "Retro football game feel.", "LIVE", "TRUECOLOR ▀", styles.pixel_retro, "live", "67'", "Ødegaard → Saka · ATTACKING THIRD"),
    Card("04", "BRAILLE RENDER", "Higher resolution graphics.", "LIVE", "UNICODE ⣿", styles.braille_pitch, "live", "67'", "Rice hits a long pass."),
    Card("05", "ISOMETRIC 2.5D", "Unique angle, retro RPG vibe.", "LIVE", "TRUECOLOR ▀", styles.isometric, "live", "67'", "Saka cuts inside..."),
    Card("06", "RAYCASTING STADIUM", "First-person match experience.", "LIVE", "TRUECOLOR ▀", styles.raycast, "live", "67'", "Early pressure from Arsenal."),
    Card("07", "VOXEL STYLE", "Blocky, charming, 3D look.", "REPLAY", "TRUECOLOR ▀", styles.voxel, "goal", "67'", "GOOOAL! Ødegaard!"),
    Card("08", "SPRITE RENDER", "Real images in terminal.", "CONCEPT", "KITTY · SIXEL", styles.sprite_render, "live", "67'", "Palmer tracks back."),
    Card("09", "TACTICAL OVERLAY", "Data meets visuals.", "ANALYSIS", "KITTY · SIXEL", styles.tactical, "live", "67'", "High press in the opposition half."),
    Card("10", "CINEMATIC REPLAY", "Beautiful goal replays.", "REPLAY", "KITTY · SIXEL", styles.cinematic, "goal", "67'", "Ødegaard → Saka → GOAL!"),
    Card("11", "BROADCAST UI", "TV-style presentation.", "LIVE", "KITTY · SIXEL", styles.broadcast, "live", "67'", "Arsenal probing for another goal..."),
    Card("12", "PARTICLE EFFECTS", "Goals hit different.", "EFFECT", "KITTY · SIXEL", styles.particles, "goal", "67'", "Ødegaard scores!"),
    Card("13", "ANALYTICS VIEW", "Deeper insights, live.", "ANALYSIS", "KITTY · SIXEL", styles.analytics, "live", "67'", "Pass network · xG · shots · momentum"),
]


def score_strip(d: ImageDraw.ImageDraw, x0, y0, x1, m: Match, goal: bool, tag: str, clock: str):
    h = 30
    d.rectangle([x0, y0, x1, y0 + h], fill=(7, 10, 14))
    fb = font(15, "Bold")
    home_score, away_score = (m.score[0] + (1 if goal else 0)), m.score[1]
    x = x0 + 8
    for code, colour, score in ((m.home.code, m.home.colour, home_score), (m.away.code, m.away.colour, away_score)):
        w = fb.getlength(code) + 14
        d.rectangle([x, y0 + 5, x + w, y0 + h - 5], fill=colour)
        d.text((x + w / 2, y0 + h / 2), code, font=fb, fill=(255, 255, 255), anchor="mm")
        x += w + 10
        if code == m.home.code:
            d.text((x, y0 + h / 2), f"{home_score}  -  {away_score}", font=font(16, "Bold"), fill=TEXT, anchor="lm")
            x += fb.getlength(f"{home_score}  -  {away_score}") + 14
    tc = TAGS.get(tag, DIM)
    tf = font(11, "Bold")
    tw = tf.getlength(tag) + 18
    d.rounded_rectangle([x1 - tw - 8, y0 + 7, x1 - 8, y0 + h - 7], radius=4, fill=shade(tc, 0.2), outline=tc, width=1)
    d.text((x1 - 8 - tw / 2, y0 + h / 2), tag, font=tf, fill=tc, anchor="mm")
    d.text((x1 - tw - 20, y0 + h / 2), clock, font=font(15, "Semibold"), fill=TEXT, anchor="rm")


def tile(card: Card, live: Match, goal: Match) -> Image.Image:
    m = goal if card.moment == "goal" else live
    art = card.render(m)
    assert art.size == ART, (card.title, art.size)
    img = Image.new("RGB", (TILE_W, TILE_H), PANEL)
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([0, 0, TILE_W - 1, TILE_H - 1], radius=10, fill=PANEL, outline=BORDER, width=2)
    d.text((16, 16), card.number, font=font(22, "Semibold"), fill=FAINT)
    d.line([(52, 18), (52, 40)], fill=BORDER, width=2)
    d.text((64, 16), card.title, font=font(22, "Bold"), fill=TEXT)
    d.text((16, 52), card.subtitle, font=font(15), fill=DIM)
    score_strip(d, 12, 80, TILE_W - 12, live, card.moment == "goal", card.tag, m.clock)
    img.paste(art, (12, ART_Y))
    d.rectangle([12, ART_Y, 12 + ART[0] - 1, ART_Y + ART[1] - 1], outline=(26, 36, 48), width=1)
    cy = ART_Y + ART[1] + 26
    d.text((16, cy), card.minute, font=font(15, "Bold"), fill=ACCENT, anchor="lm")
    d.text((56, cy), card.caption, font=font(15), fill=(196, 208, 222), anchor="lm")
    nf = font(10, "Bold")
    nw = nf.getlength(card.needs) + 16
    d.rounded_rectangle([TILE_W - 16 - nw, cy - 10, TILE_W - 16, cy + 10], radius=3, outline=(48, 62, 78), width=1)
    d.text((TILE_W - 16 - nw / 2, cy), card.needs, font=nf, fill=(120, 138, 158), anchor="mm")
    return img


def catalog(live: Match, goal: Match, tiles: list[Image.Image]) -> Image.Image:
    cols, gap, margin = 5, 20, 28
    head, foot = 150, 64
    rows = (len(tiles) + cols - 1) // cols
    W = margin * 2 + cols * TILE_W + (cols - 1) * gap
    H = head + rows * TILE_H + (rows - 1) * gap + foot
    img = Image.new("RGB", (W, H), BG)
    d = ImageDraw.Draw(img)
    d.rectangle([margin, 32, margin + 6, 112], fill=ACCENT)
    d.text((margin + 26, 30), "FM TERMINAL", font=font(54, "Heavy"), fill=TEXT)
    tw = font(54, "Heavy").getlength("FM TERMINAL")
    d.text((margin + 26 + tw + 30, 44), "LIVE MATCH VISUALIZATION CATALOG", font=font(26, "Semibold"), fill=(176, 192, 210))
    d.text((margin + 28, 96), "DIFFERENT STYLES.  SAME MATCH.  ENTIRELY IN YOUR TERMINAL.", font=font(19), fill=DIM)
    d.text((W - margin, 40), "FOOTBALL. REIMAGINED.", font=font(18, "Semibold"), fill=FAINT, anchor="ra")
    chips = ["ASCII", "UNICODE", "TRUECOLOR", "BRAILLE", "PIXEL", "2.5D", "3D", "ANALYTICS", "CINEMATIC"]
    cf = font(13, "Semibold")
    x = W - margin
    for c in reversed(chips):
        label = f"[ {c} ]"
        w = cf.getlength(label)
        d.text((x, 100), label, font=cf, fill=(110, 130, 150), anchor="rm")
        x -= w + 18
    for i, t in enumerate(tiles):
        x = margin + (i % cols) * (TILE_W + gap)
        y = head + (i // cols) * (TILE_H + gap)
        img.paste(t, (x, y))
    fy = H - foot / 2
    d.text((margin, fy), "FM TERMINAL   │   LIVE MATCH VISUALIZATION STYLES   │   BUILD YOUR OWN EXPERIENCE", font=font(15), fill=FAINT, anchor="lm")
    d.text((W - margin, fy), "SAME DATA. DIFFERENT PERSPECTIVES. ENDLESS POSSIBILITIES.", font=font(15), fill=FAINT, anchor="rm")
    return img


def build(only: list[str] | None = None):
    live, goal = load(goal=False), load(goal=True)
    cards = [c for c in CARDS if not only or c.number in only]
    tiles = [tile(c, live, goal) for c in cards]
    return cards, tiles, catalog(live, goal, tiles)
