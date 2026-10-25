"""Voxels: the pitch, stands and players as little cubes under an isometric camera."""

from __future__ import annotations

import math

import numpy as np
from PIL import Image

from .common import PITCH_L, PITCH_W, Match, shade
from .scene import Stage, markings

TOP, LEFT, FRONT = 1.0, 0.78, 0.6


class Voxels:
    def __init__(self):
        self.cubes: list[tuple[float, float, float, float, tuple, bool]] = []  # x, y, z, size, colour, outline

    def add(self, x, y, z, s, colour, outline=False):
        self.cubes.append((x, y, z, s, colour, outline))

    def draw(self, st: Stage):
        cam = st.cam
        eye_dir = -cam.R[2]  # toward the camera
        sx_face = (-1 if eye_dir[0] < 0 else 1)
        sy_face = (-1 if eye_dir[1] < 0 else 1)
        cubes = sorted(self.cubes, key=lambda c: -cam.depth((c[0] + c[3] / 2, c[1] + c[3] / 2, c[2] + c[3] / 2)))
        for x, y, z, s, col, outline in cubes:
            x1, y1, z1 = x + s, y + s, z + s
            fx = x if sx_face < 0 else x1
            fy = y if sy_face < 0 else y1
            ol = shade(col, 0.55) if outline else None
            st.poly([(x, y, z1), (x1, y, z1), (x1, y1, z1), (x, y1, z1)], shade(col, TOP), ol)
            st.poly([(fx, y, z), (fx, y1, z), (fx, y1, z1), (fx, y, z1)], shade(col, LEFT), ol)
            st.poly([(x, fy, z), (x1, fy, z), (x1, fy, z1), (x, fy, z1)], shade(col, FRONT), ol)


def pitch_tiles(v: float = 0.875):
    """Grass tiles with the markings burnt in; returns {(i, j): colour}."""
    nx, ny = int(PITCH_L / v), int(PITCH_W / v)
    rng = np.random.default_rng(4)
    tiles = {}
    for i in range(-3, nx + 3):
        for j in range(-3, ny + 3):
            stripe = int((i * v) / (PITCH_L / 14)) % 2 if 0 <= i < nx else 1
            base = (78, 166, 70) if stripe == 0 else (64, 146, 60)
            if not (0 <= i < nx and 0 <= j < ny):
                base = (52, 122, 50)
            k = rng.uniform(0.94, 1.06)
            tiles[(i, j)] = tuple(min(255, round(c * k)) for c in base)
    lines, spots = markings()
    for pl in lines:
        for (ax, ay), (bx, by) in zip(pl, pl[1:]):
            n = int(math.hypot(bx - ax, by - ay) / v * 3) + 1
            for t in range(n + 1):
                x = ax + (bx - ax) * t / n
                y = ay + (by - ay) * t / n
                i, j = min(nx - 1, int(x / v)), min(ny - 1, int(y / v))
                tiles[(i, j)] = (240, 244, 238)
    for x, y, _ in spots:
        tiles[(int(x / v), int(y / v))] = (240, 244, 238)
    return tiles, nx, ny


def figure(vox: Voxels, p, s: float = 0.5):
    """A footballer five voxels tall-ish: boots, socks, shorts, shirt with sleeves, head with hair."""
    k = p.kit
    x0, y0 = p.x - s, p.y - s / 2
    # legs (two columns)
    for dx in (0, 1):
        vox.add(x0 + dx * s, y0, 0, s, (26, 26, 30))
        vox.add(x0 + dx * s, y0, s, s, k.socks)
        vox.add(x0 + dx * s, y0, 2 * s, s, k.shorts)
    # torso three wide with sleeves
    for dx in (-0.5, 0.5, 1.5):
        if dx in (-0.5, 1.5):
            vox.add(x0 + dx * s, y0, 3 * s, s, k.sleeves)
            vox.add(x0 + dx * s, y0, 2.2 * s, s * 0.8, p.skin)
        else:
            vox.add(x0 + dx * s, y0, 3 * s, s, k.shirt)
            vox.add(x0 + dx * s, y0, 4 * s, s, k.shirt)
    vox.add(x0 + 0.5 * s, y0, 5 * s, s, p.skin)
    vox.add(x0 + 0.5 * s, y0, 6 * s, s * 0.9, p.hair)


def voxel_scene(m: Match, size, focus=(80.0, 34.0), ortho: float = 7.0, ss: int = 2, figure_size: float = 0.5) -> Image.Image:
    st = Stage(size, ss=ss, bg=(10, 14, 20))
    fx, fy = focus
    st.camera((fx - 60, fy - 72, 70), (fx, fy, 0), ortho=ortho)
    # sky gradient
    a = np.linspace(0, 1, st.H)[:, None, None]
    top, bot = np.array((12, 18, 34), float), np.array((30, 40, 64), float)
    st.img.paste(Image.fromarray((top + (bot - top) * a).repeat(st.W, axis=1).astype(np.uint8)))

    v = 0.875
    tiles, nx, ny = pitch_tiles(v)
    # the ground slab as flat quads (cheap), with a thick edge on the camera sides
    for (i, j), col in tiles.items():
        x, y = i * v, j * v
        st.poly([(x, y, 0), (x + v, y, 0), (x + v, y + v, 0), (x, y + v, 0)], col)
    edge = (60, 44, 30)
    for i in range(-3, nx + 3):
        x = i * v
        st.poly([(x, -3 * v, 0), (x + v, -3 * v, 0), (x + v, -3 * v, -1.6), (x, -3 * v, -1.6)], shade(edge, 0.8 if i % 2 else 0.72))
    for j in range(-3, ny + 3):
        y = j * v
        st.poly([(-3 * v, y, 0), (-3 * v, y + v, 0), (-3 * v, y + v, -1.6), (-3 * v, y, -1.6)], shade(edge, 0.95 if j % 2 else 0.88))

    vox = Voxels()
    rng = np.random.default_rng(9)
    # stepped stands with a voxel crowd behind the far touchline and the right goal line
    step = 1.4
    for row in range(9):
        y = PITCH_W + 3 * v + 2 + row * step
        for x in np.arange(-2, PITCH_L + 2, step):
            vox.add(x, y, row * step * 0.8, step, (64, 66, 76) if row % 2 else (56, 58, 68))
            if rng.random() < 0.8:
                c = [m.home.colour, m.away.colour, (230, 230, 230), (40, 40, 44)][rng.choice(4, p=[0.45, 0.3, 0.13, 0.12])]
                vox.add(x + 0.35, y + 0.35, row * step * 0.8 + step, 0.7, c)
    for row in range(9):
        x = PITCH_L + 3 * v + 2 + row * step
        for y in np.arange(-2, PITCH_W + 2, step):
            vox.add(x, y, row * step * 0.8, step, (64, 66, 76) if row % 2 else (56, 58, 68))
            if rng.random() < 0.8:
                c = [m.away.colour, m.home.colour, (230, 230, 230), (40, 40, 44)][rng.choice(4, p=[0.55, 0.2, 0.13, 0.12])]
                vox.add(x + 0.35, y + 0.35, row * step * 0.8 + step, 0.7, c)
    # boards
    for x in np.arange(-2, PITCH_L + 2, 1.0):
        vox.add(x, PITCH_W + 3 * v + 0.3, 0, 1.0, [(30, 40, 110), (200, 30, 40), (240, 240, 240)][int(x / 6) % 3])
    for y in np.arange(-2, PITCH_W + 2, 1.0):
        vox.add(PITCH_L + 3 * v + 0.3, y, 0, 1.0, [(240, 200, 40), (30, 40, 110), (240, 240, 240)][int(y / 6) % 3])
    # goals: white posts and bar, grey net blocks behind
    for gx, back in ((0.0, -2.0), (PITCH_L - 0.3, PITCH_L + 1.7)):
        y0, y1 = PITCH_W / 2 - 3.66, PITCH_W / 2 + 3.66
        for z in np.arange(0, 2.44, 0.3):
            vox.add(gx, y0, z, 0.3, (250, 250, 250))
            vox.add(gx, y1, z, 0.3, (250, 250, 250))
        for y in np.arange(y0, y1 + 0.3, 0.3):
            vox.add(gx, y, 2.44, 0.3, (250, 250, 250))
            vox.add(back, y, 0, 0.3, (200, 205, 210))
            vox.add(back, y, 1.2, 0.3, (200, 205, 210))
    for p in m.players:
        figure(vox, p, figure_size)
    bx, by, bz = m.ball
    vox.add(bx - 0.3, by - 0.3, bz, 0.6, (255, 255, 255), outline=True)
    vox.draw(st)
    return st.finish()
