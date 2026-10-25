"""Palette, fonts and the mock match in world units (metres, origin at a corner flag)."""

from __future__ import annotations

import hashlib
import math
import sys
from dataclasses import dataclass, field
from functools import lru_cache
from pathlib import Path

import numpy as np
from PIL import Image, ImageFont

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import mock_match  # noqa: E402

PITCH_L, PITCH_W = 105.0, 68.0

# ---------------------------------------------------------------- palette

BG = (7, 11, 15)
PANEL = (10, 15, 21)
PANEL_HI = (14, 21, 29)
BORDER = (31, 44, 58)
TEXT = (226, 234, 242)
DIM = (124, 142, 162)
FAINT = (78, 94, 112)
ACCENT = (72, 200, 220)
YELLOW = (250, 204, 72)
WHITE = (244, 246, 248)

SKINS = [(236, 196, 160), (198, 142, 102), (142, 96, 66), (96, 62, 44), (224, 176, 136), (172, 118, 82)]
HAIR = [(30, 22, 18), (58, 40, 26), (20, 16, 14), (120, 82, 44), (16, 12, 10)]


def hexrgb(value: str) -> tuple[int, int, int]:
    value = value.lstrip("#")
    return tuple(int(value[i:i + 2], 16) for i in (0, 2, 4))  # type: ignore[return-value]


def mix(a, b, t: float):
    return tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(3))


def shade(c, k: float):
    """k < 1 darkens, k > 1 lightens toward white."""
    if k <= 1:
        return tuple(max(0, round(v * k)) for v in c)
    return mix(c, (255, 255, 255), min(1.0, k - 1))


def lum(c) -> float:
    return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2]


def lift(c, floor: float = 95.0):
    """Keep a kit colour visible on a dark terminal."""
    l = lum(c)
    if l >= floor:
        return c
    return mix(c, (255, 255, 255), min(0.75, (floor - l) / 255 * 2.2))


# ---------------------------------------------------------------- fonts

SF_MONO = "/System/Library/Fonts/SFNSMono.ttf"
MENLO = "/System/Library/Fonts/Menlo.ttc"


@lru_cache(maxsize=None)
def font(size: int, weight: str = "Regular") -> ImageFont.FreeTypeFont:
    try:
        f = ImageFont.truetype(SF_MONO, size)
        f.set_variation_by_name(weight)
        return f
    except OSError:
        return ImageFont.truetype(MENLO, size, index=1 if weight in ("Bold", "Heavy", "Semibold") else 0)


@lru_cache(maxsize=None)
def menlo(size: int, bold: bool = False) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(MENLO, size, index=1 if bold else 0)


def text_w(f: ImageFont.FreeTypeFont, s: str) -> float:
    return f.getlength(s)


# ---------------------------------------------------------------- the match


@dataclass
class Kit:
    shirt: tuple
    sleeves: tuple
    shorts: tuple
    socks: tuple
    number: tuple


KITS = {
    "ARS": Kit((219, 10, 22), (250, 250, 250), (250, 250, 250), (219, 10, 22), (255, 255, 255)),
    "CHE": Kit((3, 70, 148), (3, 70, 148), (3, 70, 148), (245, 245, 245), (255, 255, 255)),
}
KEEPERS = {
    "ARS": Kit((245, 197, 24), (245, 197, 24), (30, 30, 30), (245, 197, 24), (20, 20, 20)),
    "CHE": Kit((24, 24, 28), (24, 24, 28), (24, 24, 28), (24, 24, 28), (240, 240, 240)),
}


@dataclass
class Player:
    number: int
    name: str
    team: int  # 0 home, 1 away
    x: float  # metres along, 0 = home goal line
    y: float  # metres across, 0 = near touchline
    facing: float  # degrees, 0 = toward the away goal (+x)
    keeper: bool
    kit: Kit
    skin: tuple
    hair: tuple
    phase: float  # stride phase, 0..1
    lean: float = 0.0  # degrees off vertical on screen, for dives and slides
    lift: float = 0.0  # metres off the ground

    @property
    def colour(self):
        return self.kit.shirt


@dataclass
class Team:
    code: str
    name: str
    colour: tuple
    text: tuple


@dataclass
class Match:
    home: Team
    away: Team
    players: list[Player]
    ball: tuple[float, float, float]  # metres; z is height
    clock: str
    score: tuple[int, int]
    commentary: str
    meta: dict = field(default_factory=dict)

    def team(self, i: int) -> Team:
        return self.home if i == 0 else self.away

    def of(self, team: int) -> list[Player]:
        return [p for p in self.players if p.team == team]

    def by_name(self, name: str) -> Player:
        return next(p for p in self.players if p.name == name)


def _seed(s: str) -> int:
    return int(hashlib.md5(s.encode()).hexdigest()[:8], 16)


def load(goal: bool = False, raw: dict | None = None) -> Match:
    raw = raw or mock_match.frame(goal=goal)
    meta = raw["meta"]
    teams = [meta["home"], meta["away"]]
    players = []
    b = raw["radar"]["ball"]
    bx, by = b["x"] * PITCH_L, b["y"] * PITCH_W
    for p in raw["radar"]["players"]:
        if p["sub"]:
            continue
        team = teams[p["team"]]
        keeper = p["kit"].lower() == team["keeper"].lower()
        kits = KEEPERS if keeper else KITS
        kit = kits.get(team["code"]) or Kit(*([hexrgb(p["kit"])] * 4), hexrgb(p["txt"]))
        h = _seed(p["name"])
        x, y = p["x"] * PITCH_L, p["y"] * PITCH_W
        # everyone watches the ball: the mock's rot is not a reliable heading
        facing = math.degrees(math.atan2(by - y, bx - x)) % 360
        players.append(Player(
            p["n"], p["name"], p["team"], x, y,
            facing, keeper, kit, SKINS[h % len(SKINS)], HAIR[(h >> 4) % len(HAIR)],
            ((h >> 8) % 1000) / 1000,
        ))
    st = raw["state"]
    home = Team(teams[0]["code"], teams[0]["name"], KITS.get(teams[0]["code"], Kit(*[hexrgb(teams[0]["outfield"])] * 5)).shirt, hexrgb(teams[0]["text"]))
    away = Team(teams[1]["code"], teams[1]["name"], KITS.get(teams[1]["code"], Kit(*[hexrgb(teams[1]["outfield"])] * 5)).shirt, hexrgb(teams[1]["text"]))
    return Match(
        home, away, players,
        (b["x"] * PITCH_L, b["y"] * PITCH_W, b["h"] * 2.4),
        st["MatchTime"], (int(st["HomeScore"]), int(st["AwayScore"])), st["Commentary"], meta,
    )


# ---------------------------------------------------------------- image helpers


def supersample(draw_fn, size: tuple[int, int], ss: int = 3, bg=(0, 0, 0)) -> Image.Image:
    """Run draw_fn(img, scale) on an ss-times larger canvas and shrink it: anti-aliased shapes for free."""
    w, h = size
    big = Image.new("RGB", (w * ss, h * ss), bg)
    draw_fn(big, ss)
    return big.resize((w, h), Image.Resampling.LANCZOS)


def noise(img: Image.Image, amount: float = 4.0, seed: int = 1) -> Image.Image:
    a = np.asarray(img).astype(np.float32)
    rng = np.random.default_rng(seed)
    a += rng.normal(0, amount, a.shape[:2])[..., None]
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8))


def vignette(img: Image.Image, strength: float = 0.35) -> Image.Image:
    w, h = img.size
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    d = np.sqrt(((xx - w / 2) / (w / 2)) ** 2 + ((yy - h / 2) / (h / 2)) ** 2) / 1.414
    k = 1 - strength * np.clip(d, 0, 1) ** 2
    a = np.asarray(img).astype(np.float32) * k[..., None]
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8))


def glow(img: Image.Image, radius: float = 6, gain: float = 0.8, threshold: int = 170) -> Image.Image:
    """Bloom: blur what is bright and add it back."""
    from PIL import ImageFilter

    a = np.asarray(img).astype(np.float32)
    bright = np.clip((a.max(axis=2, keepdims=True) - threshold) / (255 - threshold), 0, 1) * a
    blurred = np.asarray(Image.fromarray(bright.astype(np.uint8)).filter(ImageFilter.GaussianBlur(radius))).astype(np.float32)
    return Image.fromarray(np.clip(a + blurred * gain, 0, 255).astype(np.uint8))
