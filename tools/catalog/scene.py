"""
A small 3D stage: a camera, the pitch, goals, boards, stands with a crowd, and players.

World units are metres. x runs along the pitch (0 = home goal line, 105 = away goal line),
y across it (0 = near touchline), z up. Everything is drawn on a canvas `ss` times larger than
the output and shrunk at the end, so lines and figures come out anti-aliased.
"""

from __future__ import annotations

import math
from dataclasses import dataclass

import numpy as np
from PIL import Image, ImageDraw

from .common import PITCH_L, PITCH_W, Match, Player, mix, shade

# ---------------------------------------------------------------- camera


class Camera:
    def __init__(self, eye, target, size, fov: float = 40.0, ortho: float | None = None,
                 up=(0.0, 0.0, 1.0), near: float = 0.3, shift=(0.0, 0.0)):
        self.eye = np.asarray(eye, float)
        f = np.asarray(target, float) - self.eye
        f /= np.linalg.norm(f)
        r = np.cross(f, np.asarray(up, float))
        if np.linalg.norm(r) < 1e-6:
            r = np.cross(f, np.array([0.0, 1.0, 0.0]))
        r /= np.linalg.norm(r)
        u = np.cross(r, f)
        self.R = np.stack([r, u, f])
        self.w, self.h = size
        self.focal = (self.h / 2) / math.tan(math.radians(fov) / 2)
        self.ortho = ortho
        self.near = near
        self.cx = self.w / 2 + shift[0] * self.w
        self.cy = self.h / 2 + shift[1] * self.h

    def cam(self, p) -> np.ndarray:
        return (np.asarray(p, float) - self.eye) @ self.R.T

    def screen(self, c: np.ndarray) -> np.ndarray:
        c = np.asarray(c, float)
        if self.ortho:
            return np.stack([self.cx + c[..., 0] * self.ortho, self.cy - c[..., 1] * self.ortho], -1)
        z = np.maximum(c[..., 2], 1e-6)
        return np.stack([self.cx + self.focal * c[..., 0] / z, self.cy - self.focal * c[..., 1] / z], -1)

    def project(self, p):
        c = self.cam(p)
        if not self.ortho and c[2] < self.near:
            return None
        s = self.screen(c)
        return (float(s[0]), float(s[1]))

    def depth(self, p) -> float:
        return float(self.cam(p)[2])

    def ppm(self, p) -> float:
        """Pixels per metre at a point."""
        if self.ortho:
            return self.ortho
        return self.focal / max(self.cam(p)[2], self.near)

    def clip(self, cam_pts: np.ndarray) -> np.ndarray:
        """Sutherland–Hodgman against the near plane, in camera space."""
        if self.ortho:
            return cam_pts
        out = []
        n = len(cam_pts)
        for i in range(n):
            a, b = cam_pts[i], cam_pts[(i + 1) % n]
            ia, ib = a[2] >= self.near, b[2] >= self.near
            if ia:
                out.append(a)
            if ia != ib:
                t = (self.near - a[2]) / (b[2] - a[2])
                out.append(a + (b - a) * t)
        return np.array(out) if out else np.zeros((0, 3))


# ---------------------------------------------------------------- stage


class Stage:
    def __init__(self, size, ss: int = 3, bg=(10, 14, 20)):
        self.size = size
        self.ss = ss
        self.W, self.H = size[0] * ss, size[1] * ss
        self.img = Image.new("RGB", (self.W, self.H), bg)
        self.d = ImageDraw.Draw(self.img, "RGBA")
        self.cam: Camera | None = None

    def camera(self, eye, target, fov=40.0, ortho=None, up=(0, 0, 1), shift=(0, 0)):
        self.cam = Camera(eye, target, (self.W, self.H), fov, ortho * self.ss if ortho else None, up, shift=shift)
        return self.cam

    # -- primitives

    def poly(self, pts, fill, outline=None, width=1):
        c = self.cam.clip(self.cam.cam(np.asarray(pts, float)))
        if len(c) < 3:
            return
        s = self.cam.screen(c)
        self.d.polygon([tuple(p) for p in s], fill=fill, outline=outline, width=width)

    def line3(self, a, b, width_m: float, fill, min_px: float = 1.0):
        ca, cb = self.cam.cam(a), self.cam.cam(b)
        if not self.cam.ortho:
            if ca[2] < self.cam.near and cb[2] < self.cam.near:
                return
            if ca[2] < self.cam.near:
                ca = ca + (cb - ca) * ((self.cam.near - ca[2]) / (cb[2] - ca[2]))
            if cb[2] < self.cam.near:
                cb = cb + (ca - cb) * ((self.cam.near - cb[2]) / (ca[2] - cb[2]))
        sa, sb = self.cam.screen(ca), self.cam.screen(cb)
        mid = (np.asarray(a) + np.asarray(b)) / 2
        w = max(min_px * self.ss, width_m * self.cam.ppm(mid))
        self.d.line([tuple(sa), tuple(sb)], fill=fill, width=max(1, round(w)))

    def ribbon(self, pts2d, width: float, fill, z: float = 0.02, closed=False):
        """A painted line on the ground along a 2D polyline."""
        pts = list(pts2d) + ([pts2d[0]] if closed else [])
        hw = width / 2
        for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
            dx, dy = x1 - x0, y1 - y0
            n = math.hypot(dx, dy) or 1
            nx, ny = -dy / n * hw, dx / n * hw
            # overlap a touch so joints don't show seams
            ex, ey = dx / n * hw * 0.8, dy / n * hw * 0.8
            self.poly([(x0 - ex + nx, y0 - ey + ny, z), (x1 + ex + nx, y1 + ey + ny, z),
                       (x1 + ex - nx, y1 + ey - ny, z), (x0 - ex - nx, y0 - ey - ny, z)], fill)

    def disc(self, x, y, r, fill, z=0.02, n=20):
        self.poly([(x + r * math.cos(a), y + r * math.sin(a), z)
                   for a in np.linspace(0, 2 * math.pi, n, endpoint=False)], fill)

    def finish(self) -> Image.Image:
        return self.img.resize(self.size, Image.Resampling.LANCZOS)


# ---------------------------------------------------------------- pitch geometry


def arc(cx, cy, r, a0, a1, n=48):
    return [(cx + r * math.cos(a), cy + r * math.sin(a)) for a in np.linspace(a0, a1, n)]


def markings() -> tuple[list[list[tuple[float, float]]], list[tuple[float, float, float]]]:
    """Pitch lines as polylines plus spots (x, y, r)."""
    L, W = PITCH_L, PITCH_W
    c = W / 2
    lines = [
        [(0, 0), (L, 0), (L, W), (0, W), (0, 0)],
        [(L / 2, 0), (L / 2, W)],
        arc(L / 2, c, 9.15, 0, 2 * math.pi, 72),
    ]
    for side in (0, 1):
        sx = (lambda v: v) if side == 0 else (lambda v: L - v)
        lines.append([(sx(0), c - 20.16), (sx(16.5), c - 20.16), (sx(16.5), c + 20.16), (sx(0), c + 20.16)])
        lines.append([(sx(0), c - 9.16), (sx(5.5), c - 9.16), (sx(5.5), c + 9.16), (sx(0), c + 9.16)])
        a = math.acos(5.5 / 9.15)
        if side == 0:
            lines.append(arc(11, c, 9.15, -a, a, 24))
        else:
            lines.append(arc(L - 11, c, 9.15, math.pi - a, math.pi + a, 24))
    for (x, y, a0) in ((0, 0, 0), (L, 0, math.pi / 2), (L, W, math.pi), (0, W, 1.5 * math.pi)):
        lines.append(arc(x, y, 1.0, a0, a0 + math.pi / 2, 8))
    spots = [(L / 2, c, 0.22), (11, c, 0.2), (L - 11, c, 0.2)]
    return lines, spots


# ---------------------------------------------------------------- look


@dataclass
class Look:
    grass_a: tuple = (52, 128, 56)
    grass_b: tuple = (44, 114, 50)
    surround: tuple = (38, 98, 44)
    line: tuple = (236, 242, 236, 235)
    line_w: float = 0.14
    stripes: int = 18
    checker: bool = False
    sky_top: tuple = (8, 12, 22)
    sky_bottom: tuple = (22, 30, 44)
    stand: tuple = (40, 44, 54)
    stand_face: tuple = (26, 30, 38)
    roof: tuple = (18, 20, 26)
    crowd_light: float = 1.0
    boards: bool = True
    stands: tuple = ("far", "left", "right")
    floodlights: bool = True
    net: tuple = (235, 238, 242, 110)
    exaggerate_ball: float = 2.4
    crowd_density: float = 1.0
    crowd_calm: float = 0.0  # 0 = lively random crowd, 1 = mostly team colours with little jitter
    shadows: str = "flood"  # flood | sun | none
    outer: tuple = (16, 20, 26)
    roofs: bool = True
    cutaway: bool = False  # near stands cut down to a few rows, as isometric games do
    rise: float = 1.0  # stand height multiplier


BOARD_ADS = [
    ((18, 26, 60), (240, 240, 250), "FM CLI"),
    ((200, 16, 32), (255, 255, 255), "TERMINAL"),
    ((240, 240, 244), (20, 24, 40), "--LIVE--"),
    ((14, 110, 70), (230, 255, 240), "BRAILLE"),
    ((250, 200, 40), (30, 20, 0), "ASCII 26"),
    ((20, 40, 120), (255, 220, 60), "TRUECOLOR"),
]


def sky(st: Stage, look: Look):
    a = np.linspace(0, 1, st.H)[:, None, None]
    top, bot = np.array(look.sky_top, float), np.array(look.sky_bottom, float)
    grad = (top + (bot - top) * a).repeat(st.W, axis=1)
    st.img.paste(Image.fromarray(grad.astype(np.uint8)))


def ground(st: Stage, look: Look):
    L, W = PITCH_L, PITCH_W
    st.poly([(-80, -80, 0), (L + 80, -80, 0), (L + 80, W + 80, 0), (-80, W + 80, 0)], look.outer)
    st.poly([(-9, -8, 0), (L + 9, -8, 0), (L + 9, W + 8, 0), (-9, W + 8, 0)], look.surround)
    n = look.stripes
    for i in range(n):
        x0, x1 = L * i / n, L * (i + 1) / n
        col = look.grass_a if i % 2 == 0 else look.grass_b
        if look.checker:
            m = 8
            for j in range(m):
                y0, y1 = W * j / m, W * (j + 1) / m
                c = col if j % 2 == 0 else mix(col, look.grass_b if i % 2 == 0 else look.grass_a, 0.35)
                st.poly([(x0, y0, 0), (x1, y0, 0), (x1, y1, 0), (x0, y1, 0)], c)
        else:
            # extend the stripes over the run-off as the real mowing does
            st.poly([(x0, -3, 0), (x1, -3, 0), (x1, W + 3, 0), (x0, W + 3, 0)], col)
    lines, spots = markings()
    for pl in lines:
        st.ribbon(pl, look.line_w, look.line)
    for x, y, r in spots:
        st.disc(x, y, r, look.line)


def goal_parts(st: Stage, side: int, look: Look):
    """Draw one goal: nets first, then the frame."""
    L, c = PITCH_L, PITCH_W / 2
    gx = 0.0 if side == 0 else L
    back = -2.0 if side == 0 else L + 2.0
    y0, y1, h = c - 3.66, c + 3.66, 2.44
    net = look.net
    faint = net[:3] + (net[3] // 3,)
    # net panels (translucent), back, roof, sides
    st.poly([(back, y0, 0), (back, y1, 0), (back, y1, h * 0.85), (back, y0, h * 0.85)], faint)
    st.poly([(gx, y0, h), (gx, y1, h), (back, y1, h * 0.85), (back, y0, h * 0.85)], faint)
    for yy in (y0, y1):
        st.poly([(gx, yy, 0), (back, yy, 0), (back, yy, h * 0.85), (gx, yy, h)], faint)
    # mesh
    mesh_w = 0.025
    for t in np.linspace(0, 1, 15):
        yy = y0 + (y1 - y0) * t
        st.line3((back, yy, 0), (back, yy, h * 0.85), mesh_w, net, 0.35)
        st.line3((back, yy, h * 0.85), (gx, yy, h), mesh_w, net, 0.35)
    for t in np.linspace(0, 1, 6):
        zz = h * 0.85 * t
        st.line3((back, y0, zz), (back, y1, zz), mesh_w, net, 0.35)
    for t in np.linspace(0, 1, 5):
        xx = gx + (back - gx) * t
        st.line3((xx, y0, h + (h * 0.85 - h) * t), (xx, y1, h + (h * 0.85 - h) * t), mesh_w, net, 0.35)
        for yy in (y0, y1):
            st.line3((xx, yy, 0), (xx, yy, h + (h * 0.85 - h) * t), mesh_w, net, 0.35)
    for yy in (y0, y1):
        for t in np.linspace(0, 1, 6):
            zz = h * t
            st.line3((gx, yy, zz), (back, yy, zz * 0.85), mesh_w, net, 0.35)
    # frame
    post = (250, 250, 250)
    st.line3((gx, y0, 0), (gx, y0, h), 0.12, post, 1.2)
    st.line3((gx, y1, 0), (gx, y1, h), 0.12, post, 1.2)
    st.line3((gx, y0, h), (gx, y1, h), 0.12, post, 1.2)
    st.line3((back, y0, 0), (back, y0, h * 0.85), 0.05, (200, 200, 205), 0.6)
    st.line3((back, y1, 0), (back, y1, h * 0.85), 0.05, (200, 200, 205), 0.6)


def _perspective_coeffs(dst, src):
    """PIL PERSPECTIVE coefficients mapping output (dst) corners back to input (src) corners."""
    A, B = [], []
    for (x, y), (u, v) in zip(dst, src):
        A.append([x, y, 1, 0, 0, 0, -u * x, -u * y])
        A.append([0, 0, 0, x, y, 1, -v * x, -v * y])
        B += [u, v]
    return np.linalg.solve(np.array(A, float), np.array(B, float)).tolist()


def texture_quad(st: Stage, corners3d, tex: Image.Image):
    """Paint an image onto a 3D quad (corners: bottom-left, bottom-right, top-right, top-left)."""
    pts = [st.cam.project(c) for c in corners3d]
    if any(p is None for p in pts):
        return False
    xs, ys = [p[0] for p in pts], [p[1] for p in pts]
    x0, y0 = int(max(0, min(xs))), int(max(0, min(ys)))
    x1, y1 = int(min(st.W, max(xs) + 1)), int(min(st.H, max(ys) + 1))
    if x1 - x0 < 2 or y1 - y0 < 2:
        return False
    if (x1 - x0) * (y1 - y0) > st.W * st.H or any(abs(c) > 4 * max(st.W, st.H) for p in pts for c in p):
        return False  # a board right at the lens: not worth a texture
    tw, th = tex.size
    dst = [(p[0] - x0, p[1] - y0) for p in pts]
    src = [(0, th), (tw, th), (tw, 0), (0, 0)]
    co = _perspective_coeffs(dst, src)
    rgba = tex.convert("RGBA")
    warped = rgba.transform((x1 - x0, y1 - y0), Image.Transform.PERSPECTIVE, co, Image.Resampling.BICUBIC)
    st.img.paste(warped, (x0, y0), warped)
    return True


def board_texture(bg, fg, word) -> Image.Image:
    from .common import font as _font
    img = Image.new("RGB", (320, 40), bg)
    d = ImageDraw.Draw(img)
    d.text((160, 21), word, font=_font(28, "Heavy"), fill=fg, anchor="mm")
    return img


def boards(st: Stage, which: str, look: Look):
    """Advertising boards along one side: 'near', 'far', 'left', 'right'."""
    L, W = PITCH_L, PITCH_W
    h = 0.95
    if which in ("near", "far"):
        y = -4.0 if which == "near" else W + 4.0
        segs = [(x, y, x + 7.5, y) for x in np.arange(-2, L + 2, 7.5)]
    else:
        x = -5.0 if which == "left" else L + 5.0
        segs = [(x, y, x, y + 7.5) for y in np.arange(-2, W + 2, 7.5)]
    for i, (x0, y0, x1, y1) in enumerate(segs):
        bg, fg, word = BOARD_ADS[(i + (3 if which in ("left", "right") else 0)) % len(BOARD_ADS)]
        st.poly([(x0, y0, 0), (x1, y1, 0), (x1, y1, h), (x0, y0, h)], bg)
        pa, pb = st.cam.project((x0, y0, h / 2)), st.cam.project((x1, y1, h / 2))
        if pa and pb and math.hypot(pb[0] - pa[0], pb[1] - pa[1]) > 60 * st.ss / 3:
            texture_quad(st, [(x0, y0, 0), (x1, y1, 0), (x1, y1, h), (x0, y0, h)], board_texture(bg, fg, word.strip("-")))
            continue
        # a band of "lettering": blocks that read as words at a distance
        rng = np.random.default_rng(i * 7 + len(which))
        n = len(word)
        for k in range(n):
            if word[k] in " -":
                continue
            t0 = 0.18 + 0.64 * k / n
            t1 = t0 + 0.64 / n * 0.72
            ax, ay = x0 + (x1 - x0) * t0, y0 + (y1 - y0) * t0
            bx, by = x0 + (x1 - x0) * t1, y0 + (y1 - y0) * t1
            st.poly([(ax, ay, 0.28), (bx, by, 0.28), (bx, by, 0.68), (ax, ay, 0.68)], fg)


@dataclass
class StandBox:
    """A stand: a raked bank of seats between two edges, plus a roof."""
    a: tuple  # front-left (x, y) at pitch side
    b: tuple  # front-right
    out: tuple  # unit vector pointing away from the pitch
    depth: float = 26.0
    rise: float = 15.0
    front_h: float = 2.2
    home_share: float = 0.5  # how much of the crowd wears home colours


def stand_boxes() -> dict[str, StandBox]:
    L, W = PITCH_L, PITCH_W
    return {
        "far": StandBox((-34, W + 8), (L + 34, W + 8), (0, 1), 30, 18, 2.4, 0.6),
        "near": StandBox((L + 34, -8), (-34, -8), (0, -1), 30, 18, 2.4, 0.6),
        "left": StandBox((-9, -6), (-9, W + 6), (-1, 0), 24, 14, 2.2, 0.92),
        "right": StandBox((L + 9, W + 6), (L + 9, -6), (1, 0), 24, 14, 2.2, 0.15),
    }


def stand(st: Stage, sb: StandBox, look: Look, home_col, away_col, seed: int):
    ax, ay = sb.a
    bx, by = sb.b
    ox, oy = sb.out
    D, H, F = sb.depth, sb.rise, sb.front_h

    def at(t, s, z_extra=0.0):
        """t along the stand (0..1), s up the rake (0..1)."""
        x = ax + (bx - ax) * t + ox * D * s
        y = ay + (by - ay) * t + oy * D * s
        return (x, y, F + H * s + z_extra)

    # back wall behind everything
    if look.roofs:
        st.poly([at(0, 1), at(1, 1), at(1, 1, 7), at(0, 1, 7)], shade(look.stand_face, 0.8))
    # seating bank
    st.poly([at(0, 0), at(1, 0), at(1, 1), at(0, 1)], look.stand)
    # crowd, vectorised
    length = math.hypot(bx - ax, by - ay)
    rows = int(D / 0.85)
    cols = int(length / 0.62 * look.crowd_density)
    rng = np.random.default_rng(seed)
    t = (np.arange(cols)[None, :] + 0.5 + rng.uniform(-0.25, 0.25, (rows, cols))) / cols
    s = (np.arange(rows)[:, None] + 0.35 + np.zeros((rows, cols))) / rows
    t, s = t.ravel(), s.ravel()
    keep = rng.random(t.size) > 0.06  # a few empty seats
    t, s = t[keep], s[keep]
    P = np.stack([ax + (bx - ax) * t + ox * D * s, ay + (by - ay) * t + oy * D * s, F + H * s], -1)
    C = st.cam.cam(P)
    ok = C[:, 2] > st.cam.near if not st.cam.ortho else np.ones(len(C), bool)
    P, C, t, s = P[ok], C[ok], t[ok], s[ok]
    S = st.cam.screen(C)
    ppm = np.full(len(C), st.cam.ortho) if st.cam.ortho else st.cam.focal / C[:, 2]
    n = len(P)
    pick = rng.random(n)
    team_is_home = pick < sb.home_share
    base = np.where(team_is_home[:, None], np.array(home_col), np.array(away_col)).astype(float)
    other = rng.random(n)
    palette = np.array([(30, 30, 34), (210, 210, 214), (60, 64, 80), (120, 40, 40), (34, 50, 90), (200, 180, 140)], float)
    neutral = palette[rng.integers(0, len(palette), n)]
    cols_arr = np.where((other < 0.62 + 0.3 * look.crowd_calm)[:, None], base, neutral)
    light = (0.55 + 0.45 * (1 - s)) * look.crowd_light  # darker toward the roof
    cols_arr = np.clip(cols_arr * light[:, None] * rng.uniform(0.75 + 0.17 * look.crowd_calm, 1.1 - 0.06 * look.crowd_calm, (n, 1)), 0, 255).astype(int)
    skin = np.array([(230, 190, 160), (180, 130, 95), (120, 82, 58)])[rng.integers(0, 3, n)] * light[:, None]
    order = np.argsort(-C[:, 2])
    for i in order:
        sx, sy = S[i]
        k = ppm[i]
        bw = 0.46 * k
        bh = 0.55 * k
        c = tuple(cols_arr[i])
        if bw < 1.6:
            st.d.rectangle([sx - 0.8, sy - bh - 0.8, sx + 0.8, sy], fill=c)
            continue
        st.d.rectangle([sx - bw / 2, sy - bh, sx + bw / 2, sy], fill=c)
        hr = 0.13 * k
        hc = tuple(int(v) for v in skin[i])
        st.d.ellipse([sx - hr, sy - bh - hr * 2.1, sx + hr, sy - bh - 0.1 * k], fill=hc)
    # front wall facing the pitch
    st.poly([(ax, ay, 0), (bx, by, 0), at(1, 0), at(0, 0)], look.stand_face)
    if not look.roofs:
        return
    # roof edge overhanging the top rows
    st.poly([at(0, 1, 7), at(1, 1, 7), at(1, 0.45, 9), at(0, 0.45, 9)], look.roof)
    st.line3(at(0, 0.45, 9), at(1, 0.45, 9), 0.3, (60, 64, 74), 0.6)


def floodlight_glow(st: Stage, look: Look):
    """Four floodlight banks high at the corners: a lamp grid with a soft halo."""
    from PIL import ImageFilter
    L, W = PITCH_L, PITCH_W
    halo = Image.new("L", (st.W, st.H), 0)
    hd = ImageDraw.Draw(halo)
    lamps = []
    for (x, y) in ((-20, -18), (L + 20, -18), (L + 20, W + 18), (-20, W + 18)):
        p = st.cam.project((x, y, 42))
        if not p:
            continue
        k = st.cam.ppm((x, y, 42))
        r = max(3.0, 3.4 * k)
        if r > st.W / 4 or not (-st.W * 0.2 < p[0] < st.W * 1.2 and -st.H * 0.2 < p[1] < st.H * 1.2):
            continue
        hd.ellipse([p[0] - r * 3, p[1] - r * 2, p[0] + r * 3, p[1] + r * 2], fill=150)
        lamps.append((p, r))
    if not lamps:
        return
    blur = max(6, int(max(r for _, r in lamps) * 2.2))
    halo = halo.filter(ImageFilter.GaussianBlur(blur))
    st.img.paste(Image.new("RGB", st.img.size, (255, 248, 226)), (0, 0), halo)
    for (px, py), r in lamps:
        st.d.rectangle([px - r, py - r * 0.5, px + r, py + r * 0.5], fill=(60, 62, 70))
        for i in range(4):
            for j in range(2):
                cx = px - r + (i + 0.5) * (2 * r / 4)
                cy = py - r * 0.5 + (j + 0.5) * r / 2
                lr = r / 5
                st.d.ellipse([cx - lr, cy - lr, cx + lr, cy + lr], fill=(255, 255, 246))


# ---------------------------------------------------------------- figures


def shadow(st: Stage, x, y, look: Look, size=1.0, strength=1.0):
    if look.shadows == "none":
        return
    if look.shadows == "sun":
        offs = [((0.55, 0.35), 0.55, 1.0)]
    else:
        offs = [((0.6, 0.5), 0.36, 0.5), ((-0.6, 0.5), 0.36, 0.5), ((0.6, -0.5), 0.36, 0.5), ((-0.6, -0.5), 0.36, 0.5), ((0, 0), 0.32, 0.8)]
    for (dx, dy), r, a in offs:
        cx, cy = x + dx * size * 0.75, y + dy * size * 0.75
        pts = []
        ang = math.atan2(dy, dx) if (dx or dy) else 0
        for k in np.linspace(0, 2 * math.pi, 16, endpoint=False):
            ex = math.cos(k) * (0.75 if (dx or dy) else 0.42) * size
            ey = math.sin(k) * 0.26 * size
            pts.append((cx + ex * math.cos(ang) - ey * math.sin(ang), cy + ex * math.sin(ang) + ey * math.cos(ang), 0.03))
        st.poly(pts, (0, 0, 0, int(70 * a * strength)))


def player_figure(st: Stage, p: Player, detail: float = 1.0, scale: float = 1.0, highlight=None, number_front: bool = False):
    """A footballer as a shaded billboard figure standing at (p.x, p.y)."""
    cam = st.cam
    foot = cam.project((p.x, p.y, p.lift))
    head = cam.project((p.x, p.y, p.lift + 1.85 * scale))
    if not foot or not head:
        return
    fx, fy = foot
    ux, uy = head[0] - fx, head[1] - fy
    hp = math.hypot(ux, uy)
    if p.lean:
        a = math.radians(p.lean)
        ux, uy = ux * math.cos(a) - uy * math.sin(a), ux * math.sin(a) + uy * math.cos(a)
    k = cam.ppm((p.x, p.y, 0.9)) * scale  # pixels per metre across
    if hp < 0.25 * k:  # looking almost straight down: fall back to a disc
        r = 0.55 * k
        st.d.ellipse([fx - r, fy - r, fx + r, fy + r], fill=p.kit.shirt, outline=shade(p.kit.shirt, 0.5), width=max(1, round(k * 0.1)))
        return
    ux, uy = ux / hp, uy / hp
    rx, ry = -uy, ux
    if rx < 0:
        rx, ry = -rx, -ry
    hs = hp / 1.85  # pixels per metre up

    # which way the figure faces on screen, for the stride and the arm swing
    fa = math.radians(p.facing)
    ahead = cam.project((p.x + math.cos(fa) * 0.6, p.y + math.sin(fa) * 0.6, 0))
    side = 0.0
    if ahead:
        side = ((ahead[0] - fx) * rx + (ahead[1] - fy) * ry) / max(1e-6, 0.6 * k)
    toward_cam = cam.cam((p.x + math.cos(fa), p.y + math.sin(fa), 0))[2] < cam.cam((p.x, p.y, 0))[2]
    stride = math.sin(p.phase * 2 * math.pi)

    def P(a, b):
        return (fx + rx * a * k + ux * b * hs, fy + ry * a * k + uy * b * hs)

    def quad(a0, b0, a1, b1, col):
        st.d.polygon([P(a0, b0), P(a1, b0), P(a1, b1), P(a0, b1)], fill=col)

    def limb(a0, b0, a1, b1, w, col):
        st.d.line([P(a0, b0), P(a1, b1)], fill=col, width=max(1, round(w * k)))
        r = w * k / 2
        for (a, b) in ((a0, b0), (a1, b1)):
            x, y = P(a, b)
            st.d.ellipse([x - r, y - r, x + r, y + r], fill=col)

    kit = p.kit
    dark = 0.62
    swing = 0.22 * stride * (0.4 + 0.6 * abs(side))
    if highlight:
        r = 0.75 * k
        st.d.ellipse([fx - r * 1.3, fy - r * 0.45, fx + r * 1.3, fy + r * 0.45], outline=highlight, width=max(1, round(k * 0.12)))

    # legs: back leg first
    for sgn, tone in ((-1, dark), (1, 1.0)):
        off = 0.11 * sgn
        foot_a = off + swing * sgn
        knee_a = off + swing * sgn * 0.5
        limb(knee_a, 0.5, foot_a, 0.1, 0.15, shade(kit.socks, tone))
        limb(knee_a * 0.9, 0.82, knee_a, 0.5, 0.17, shade(p.skin, tone))
        boot = (24, 24, 28) if tone == 1 else (14, 14, 16)
        limb(foot_a, 0.06, foot_a + 0.12 * (1 if side >= 0 else -1), 0.04, 0.12, boot)
    # shorts
    st.d.polygon([P(-0.22, 0.98), P(0.22, 0.98), P(0.24, 0.72), P(0.02, 0.7), P(-0.02, 0.7), P(-0.24, 0.72)], fill=kit.shorts)
    st.d.polygon([P(0.06, 0.98), P(0.22, 0.98), P(0.24, 0.72), P(0.06, 0.71)], fill=shade(kit.shorts, 0.82))
    # back arm
    limb(-0.27, 1.42, -0.3 - swing * 0.6, 1.08, 0.13, shade(kit.sleeves, dark))
    limb(-0.3 - swing * 0.6, 1.08, -0.28 - swing, 0.86, 0.11, shade(p.skin, dark))
    # torso
    torso = [P(-0.25, 1.5), P(0.25, 1.5), P(0.22, 1.0), P(-0.22, 1.0)]
    st.d.polygon(torso, fill=kit.shirt)
    st.d.polygon([P(0.08, 1.5), P(0.25, 1.5), P(0.22, 1.0), P(0.07, 1.0)], fill=shade(kit.shirt, 0.8))
    st.d.polygon([P(-0.25, 1.5), P(-0.14, 1.5), P(-0.13, 1.0), P(-0.22, 1.0)], fill=shade(kit.shirt, 1.12))
    # number on the back (or front) once the figure is big enough
    if detail >= 1 and hp > 26 * st.ss / 3 and (not toward_cam or number_front):
        from .common import font as _font
        size = max(6, round(0.3 * hs))
        f = _font(size, "Bold")
        cx, cy = P(0, 1.27)
        st.d.text((cx, cy), str(p.number), font=f, fill=kit.number, anchor="mm")
    # front arm
    limb(0.27, 1.42, 0.3 + swing * 0.6, 1.08, 0.13, kit.sleeves)
    limb(0.3 + swing * 0.6, 1.08, 0.28 + swing, 0.86, 0.11, p.skin)
    # neck + head
    limb(0, 1.5, 0, 1.58, 0.1, shade(p.skin, 0.85))
    hx, hy = P(0, 1.68)
    hr = 0.125 * k
    st.d.ellipse([hx - hr, hy - hr * 1.1, hx + hr, hy + hr * 1.1], fill=p.skin)
    if toward_cam:
        st.d.chord([hx - hr * 1.02, hy - hr * 1.18, hx + hr * 1.02, hy + hr * 0.8], 180, 360, fill=p.hair)
    else:
        st.d.ellipse([hx - hr * 1.02, hy - hr * 1.15, hx + hr * 1.02, hy + hr * 0.9], fill=p.hair)


def ball(st: Stage, b, look: Look, glow_col=None):
    x, y, z = b
    shadow(st, x, y, Look(shadows="sun"), size=0.35, strength=1.3)
    c = st.cam.project((x, y, z + 0.11))
    if not c:
        return
    r = max(1.6 * st.ss, 0.11 * look.exaggerate_ball * st.cam.ppm((x, y, z)))
    cx, cy = c
    if glow_col:
        for i in range(5, 0, -1):
            rr = r * (1 + i * 0.7)
            st.d.ellipse([cx - rr, cy - rr, cx + rr, cy + rr], fill=glow_col[:3] + (int(glow_col[3] / i),))
    st.d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(196, 198, 204))
    st.d.ellipse([cx - r * 0.92, cy - r * 0.94, cx + r * 0.8, cy + r * 0.78], fill=(250, 250, 250))
    if r > 5:
        for ox, oy, rr in ((0.18, 0.12, 0.26), (-0.5, 0.38, 0.17), (0.58, -0.36, 0.16), (-0.34, -0.55, 0.15)):
            st.d.ellipse([cx + (ox - rr) * r, cy + (oy - rr) * r, cx + (ox + rr) * r, cy + (oy + rr) * r], fill=(44, 46, 54))
        st.d.ellipse([cx - r * 0.62, cy - r * 0.7, cx - r * 0.2, cy - r * 0.36], fill=(255, 255, 255, 220))


# ---------------------------------------------------------------- the whole stadium


def stadium(st: Stage, m: Match, look: Look | None = None, highlight: dict | None = None,
            ball_glow=None, players: list[Player] | None = None, scale: float = 1.0):
    """Draw sky, stands, pitch, boards, goals, figures and ball in the right order for the camera."""
    look = look or Look()
    cam = st.cam
    sky(st, look)
    if look.floodlights:
        floodlight_glow(st, look)
    boxes = stand_boxes()
    home_col, away_col = m.home.colour, m.away.colour
    # stands far from the camera first
    names = [n for n in look.stands if n in boxes]
    names.sort(key=lambda n: -cam.depth((*(np.add(boxes[n].a, boxes[n].b) / 2), 5)))

    def facing_camera(n):
        sb = boxes[n]
        return (cam.eye[0] - sb.a[0]) * sb.out[0] + (cam.eye[1] - sb.a[1]) * sb.out[1] > 0

    def build(n, i):
        sb = boxes[n]
        if look.rise != 1.0:
            sb = StandBox(sb.a, sb.b, sb.out, sb.depth, sb.rise * look.rise, sb.front_h, sb.home_share)
        if look.cutaway and facing_camera(n):
            sb = StandBox(sb.a, sb.b, sb.out, 12.0, 4.0, 1.0, sb.home_share)
            stand(st, sb, Look(**{**look.__dict__, "roofs": False}), home_col, away_col, seed=11 + i)
        else:
            stand(st, sb, look, home_col, away_col, seed=11 + i)

    ground(st, look)
    late = []
    for i, n in enumerate(names):
        if facing_camera(n):
            late.append((n, i))
        else:
            build(n, i)
    sides = ["near", "far", "left", "right"]
    eye = cam.eye

    def board_is_near(side):
        if side == "near":
            return eye[1] < -4
        if side == "far":
            return eye[1] > PITCH_W + 4
        if side == "left":
            return eye[0] < -5
        return eye[0] > PITCH_L + 5

    if look.boards:
        for s in sides:
            if not board_is_near(s):
                boards(st, s, look)
    people = players if players is not None else m.players
    for p in people:
        shadow(st, p.x, p.y, look)
    # depth-sorted actors
    actors = [(cam.depth((p.x, p.y, 0.9)), "p", p) for p in people]
    for side in (0, 1):
        gx = 0.0 if side == 0 else PITCH_L
        actors.append((cam.depth((gx - 1 if side == 0 else gx + 1, PITCH_W / 2, 1.2)), "g", side))
    actors.append((cam.depth(m.ball), "b", None))
    actors.sort(key=lambda a: -a[0])
    for _, kind, obj in actors:
        if kind == "p":
            hl = (highlight or {}).get(obj.name)
            player_figure(st, obj, highlight=hl, scale=scale)
        elif kind == "g":
            goal_parts(st, obj, look)
        else:
            ball(st, m.ball, look, ball_glow)
    if look.boards:
        for s in sides:
            if board_is_near(s):
                boards(st, s, look)
    for n, i in late:
        build(n, i)
