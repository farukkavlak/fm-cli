"""The catalog styles. Each takes the match and a size and returns an image of exactly that size."""

from __future__ import annotations

import math

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

from . import term
from .common import PITCH_L, PITCH_W, Match, font, lum, mix, noise, shade, supersample, vignette
from .scene import Look, Stage, markings, stadium

ART = (560, 360)


# ---------------------------------------------------------------- top-down helpers


class Flat:
    """World metres -> 2D canvas pixels for top-down pictures (far touchline at the top)."""

    def __init__(self, box: tuple[float, float, float, float]):
        self.x0, self.y0, self.x1, self.y1 = box

    def __call__(self, x, y):
        return (self.x0 + x / PITCH_L * (self.x1 - self.x0), self.y0 + (1 - y / PITCH_W) * (self.y1 - self.y0))

    @property
    def k(self):
        return (self.x1 - self.x0) / PITCH_L


def pitch_box(size, pad_x: float, pad_y: float):
    w, h = size
    aw, ah = w - 2 * pad_x, h - 2 * pad_y
    if aw / ah > PITCH_L / PITCH_W:
        pw, ph = ah * PITCH_L / PITCH_W, ah
    else:
        pw, ph = aw, aw * PITCH_W / PITCH_L
    x0, y0 = (w - pw) / 2, (h - ph) / 2
    return (x0, y0, x0 + pw, y0 + ph)


def flat_lines(d: ImageDraw.ImageDraw, f: Flat, col, width: float, goals: bool = True):
    lines, spots = markings()
    for pl in lines:
        d.line([f(x, y) for x, y in pl], fill=col, width=max(1, round(width)), joint="curve")
    for x, y, r in spots:
        cx, cy = f(x, y)
        rr = max(width * 0.9, r * f.k * 1.6)
        d.ellipse([cx - rr, cy - rr, cx + rr, cy + rr], fill=col)
    if goals:
        for gx, back in ((0, -2.0), (PITCH_L, PITCH_L + 2.0)):
            a = f(gx, PITCH_W / 2 - 3.66)
            b = f(back, PITCH_W / 2 + 3.66)
            d.rectangle([min(a[0], b[0]), min(a[1], b[1]), max(a[0], b[0]), max(a[1], b[1])], outline=col, width=max(1, round(width)))


# ---------------------------------------------------------------- 01 / 02 ASCII


def _ascii_pitch(g: term.CharGrid, cell_aspect: float, line_fg, prio=1.0, colour=False):
    """Rasterise the pitch markings into a character grid with glyphs that follow the line."""
    cols, rows = g.cols, g.rows
    mx, my = 2, 1
    sx = (cols - 1 - 2 * mx) / PITCH_L
    sy = (rows - 1 - 2 * my) / PITCH_W

    def cell(x, y):
        return mx + x * sx, my + (PITCH_W - y) * sy

    lines, spots = markings()
    lines = [pl for pl in lines if len(pl) <= 5]  # straight lines only; the arcs are drawn row by row below
    best: dict[tuple[int, int], tuple[float, str]] = {}
    for pl in lines:
        pts = [cell(x, y) for x, y in pl]
        for (ax, ay), (bx, by) in zip(pts, pts[1:]):
            n = max(2, int(math.hypot(bx - ax, by - ay) * 6))
            dx, dy = bx - ax, (by - ay) * cell_aspect
            ang = math.degrees(math.atan2(-dy, dx)) % 180
            if ang < 22 or ang > 158:
                ch = "-"
            elif 68 < ang < 112:
                ch = "|"
            else:
                ch = "/" if ang < 90 else "\\"
            for i in range(n + 1):
                t = i / n
                cx, cy = ax + (bx - ax) * t, ay + (by - ay) * t
                c, r = round(cx), round(cy)
                dist = math.hypot(cx - c, (cy - r) * cell_aspect)
                if (c, r) not in best or dist < best[(c, r)][0]:
                    best[(c, r)] = (dist, ch)
    for (c, r), (_, ch) in best.items():
        g.put(c, r, ch, line_fg, prio=prio)

    # centre circle, the classic way:  .---.  /     \  |     |  \     /  '---'
    ccx, ccy = cell(PITCH_L / 2, PITCH_W / 2)
    rx, ry = 9.15 * sx, 9.15 * sy
    R = max(2, round(ry))
    for dr in range(-R, R + 1):
        t = dr / (R + 0.35)
        hw = rx * math.sqrt(max(0.0, 1 - t * t))
        r = round(ccy) + dr
        lc, rc = round(ccx - hw), round(ccx + hw)
        if abs(dr) == R:
            g.put(lc, r, "." if dr < 0 else "'", line_fg, prio=prio)
            g.put(rc, r, "." if dr < 0 else "'", line_fg, prio=prio)
            for c in range(lc + 1, rc):
                if c != round(ccx):
                    g.put(c, r, "-", line_fg, prio=prio)
        elif abs(t) < 0.45:
            g.put(lc, r, "|", line_fg, prio=prio)
            g.put(rc, r, "|", line_fg, prio=prio)
        else:
            g.put(lc, r, "/" if dr < 0 else "\\", line_fg, prio=prio)
            g.put(rc, r, "\\" if dr < 0 else "/", line_fg, prio=prio)
    # penalty arcs: the part of the circle round the spot outside the box, curling in at the ends
    for side in (0, 1):
        spot = 11 if side == 0 else PITCH_L - 11
        _, scy = cell(spot, PITCH_W / 2)
        edge = round(cell(16.5 if side == 0 else PITCH_L - 16.5, 0)[0])
        Ra = round(9.15 * sy * math.sin(math.acos(5.5 / 9.15)))
        arc_cells = []
        for dr in range(-Ra, Ra + 1):
            dy = dr / sy
            if abs(dy) > 9.15:
                continue
            x = spot + math.sqrt(9.15 ** 2 - dy ** 2) * (1 if side == 0 else -1)
            c = round(cell(x, 0)[0])
            if (side == 0 and c <= edge) or (side == 1 and c >= edge):
                continue
            arc_cells.append((dr, c))
        if not arc_cells:
            continue
        far = max(c for _, c in arc_cells) if side == 0 else min(c for _, c in arc_cells)
        for dr, c in arc_cells:
            if c == far:
                ch = ")" if side == 0 else "("
            elif side == 0:
                ch = "\\" if dr < 0 else "/"
            else:
                ch = "/" if dr < 0 else "\\"
            g.put(c, round(scy) + dr, ch, line_fg, prio=prio)
    # corners and joints
    joints = [(0, 0), (PITCH_L, 0), (0, PITCH_W), (PITCH_L, PITCH_W), (PITCH_L / 2, 0), (PITCH_L / 2, PITCH_W)]
    for side in (0, 1):
        for depth, half in ((16.5, 20.16), (5.5, 9.16)):
            x = depth if side == 0 else PITCH_L - depth
            for yy in (PITCH_W / 2 - half, PITCH_W / 2 + half):
                joints += [(x, yy), (0 if side == 0 else PITCH_L, yy)]
    for x, y in joints:
        c, r = cell(x, y)
        g.put(round(c), round(r), "+", line_fg, prio=prio + 0.1)
    for x, y, _ in spots:
        c, r = cell(x, y)
        g.put(round(c), round(r), "·" if colour else ".", line_fg, prio=prio + 0.05)
    # goals stick out of the goal line
    for gx, off in ((0, -1), (PITCH_L, 1)):
        c = round(cell(gx, 0)[0]) + off
        r0 = round(cell(gx, PITCH_W / 2 + 3.66)[1])
        r1 = round(cell(gx, PITCH_W / 2 - 3.66)[1])
        for rr in range(r0, r1 + 1):
            g.put(c, rr, "#" if r0 < rr < r1 else "+", line_fg, prio=prio + 0.2)
    return cell


def vivid(c, target: float = 150.0):
    """A kit colour bright enough to read as a glyph on black, keeping its hue."""
    l = lum(c)
    if l >= target:
        return c
    k = min(255 / max(1, max(c)), target / max(1, l))
    c2 = tuple(min(255, round(v * k)) for v in c)
    l2 = lum(c2)
    if l2 < target:
        c2 = mix(c2, (255, 255, 255), (target - l2) / (255 - l2))
    return c2


ASCII_CELLS = (66, 25)


def ascii_classic_grid(m: Match, cols: int, rows: int, cell_aspect: float) -> term.CharGrid:
    g = term.CharGrid(cols, rows, bg=(6, 8, 10))
    line = (150, 160, 170)
    cell = _ascii_pitch(g, cell_aspect, line)
    for p in m.players:
        c, r = cell(p.x, p.y)
        letter = m.team(p.team).code[0]
        g.put(round(c), round(r), letter, (250, 250, 250), prio=9, bold=True)
    c, r = cell(m.ball[0], m.ball[1])
    g.put(round(c), round(r), "o", (250, 250, 250), prio=10, bold=True)
    return g


def _png_aspect(size, cols, rows):
    return (size[1] / rows) / (size[0] / cols)


def ascii_classic(m: Match, size=ART) -> Image.Image:
    return ascii_classic_grid(m, *ASCII_CELLS, _png_aspect(size, *ASCII_CELLS)).render(size)


def ascii_colour_grid(m: Match, cols: int, rows: int, cell_aspect: float) -> term.CharGrid:
    stripe_a, stripe_b = (10, 42, 22), (13, 50, 27)
    g = term.CharGrid(cols, rows, bg=(5, 18, 10))
    mx = 2
    for c in range(cols):
        x = (c - mx) / (cols - 1 - 2 * mx) * PITCH_L
        band = int(x / (PITCH_L / 12)) if 0 <= x <= PITCH_L else -1
        for r in range(rows):
            if band >= 0 and 1 <= r <= rows - 2:
                g.paint_bg(c, r, stripe_a if band % 2 == 0 else stripe_b)
    line = (168, 230, 190)
    cell = _ascii_pitch(g, cell_aspect, line, colour=True)
    for p in m.players:
        c, r = cell(p.x, p.y)
        col = vivid(p.kit.shirt, 120)
        letter = m.team(p.team).code[0]
        g.put(round(c), round(r), letter, col, prio=9, bold=True)
    c, r = cell(m.ball[0], m.ball[1])
    g.put(round(c), round(r), "o", (255, 226, 92), prio=10, bold=True)
    return g


def ascii_colour(m: Match, size=ART) -> Image.Image:
    return ascii_colour_grid(m, *ASCII_CELLS, _png_aspect(size, *ASCII_CELLS)).render(size, glow=True)


# ---------------------------------------------------------------- 03 TRUECOLOR PIXEL

# 5x7 sprites, two strides. h hair, f face, s sleeve, S shirt, D shirt shade, k skin, P shorts, l sock, b boot
SPRITE = {
    0: [".hhh.", ".fff.", "sSSDs", "kSSDk", ".PPP.", ".l.l.", ".b.b."],
    1: [".hhh.", ".fff.", "sSSDs", "kSSD.", ".PPP.", "l..l.", "b...b"],
    2: [".hhh.", ".fff.", "sSSDs", ".SSDk", ".PPP.", ".l..l", "b..b."],
}
# 3x4 sprites for a small pitch (a narrow terminal)
SPRITE_SMALL = {
    0: [".f.", "sSs", ".P.", "l.l"],
    1: [".f.", "sSs", ".P.", ".ll"],
    2: [".f.", "sSs", ".P.", "ll."],
}


def _sprite(px: Image.Image, x: int, y: int, p, pose: int, small: bool = False):
    kit = p.kit
    colours = {
        "h": p.hair, "f": p.skin, "k": shade(p.skin, 0.9), "s": kit.sleeves, "S": kit.shirt,
        "D": shade(kit.shirt, 0.72), "P": kit.shorts, "l": kit.socks, "b": (20, 20, 22),
    }
    for r, line in enumerate((SPRITE_SMALL if small else SPRITE)[pose]):
        for c, ch in enumerate(line):
            if ch != ".":
                xx, yy = x + c, y + r
                if 0 <= xx < px.width and 0 <= yy < px.height:
                    px.putpixel((xx, yy), colours[ch])


def pixel_canvas(m: Match, W: int = 140, H: int = 90) -> Image.Image:
    """The retro pitch as a W x H pixel canvas; each pair of rows is one terminal cell."""
    px = Image.new("RGB", (W, H), (34, 92, 40))
    d = ImageDraw.Draw(px)
    rng = np.random.default_rng(3)
    # crowd and boards across the top; a short terminal gets a thinner band
    crowd = 7 if H >= 80 else 4
    board = 3 if H >= 80 else 2
    for x in range(W):
        for y in range(0, crowd):
            base = [m.home.colour, m.away.colour, (40, 40, 48), (210, 210, 214), (24, 24, 30)][rng.integers(0, 5)]
            px.putpixel((x, y), shade(base, 0.45 + 0.6 * y / crowd))
    ads = [((20, 30, 70), (240, 240, 250)), ((200, 20, 30), (255, 255, 255)), ((245, 245, 245), (30, 40, 80)), ((250, 200, 40), (40, 30, 0))]
    for i, x in enumerate(range(0, W, 18)):
        bg, fg = ads[i % len(ads)]
        d.rectangle([x, crowd, x + 17, crowd + board - 1], fill=bg)
        for k in range(3, 15, 2):
            if x + k < W:
                px.putpixel((x + k, crowd + board // 2), fg)
    top = crowd + board
    d.line([(0, top), (W, top)], fill=(24, 70, 30))
    # pitch
    y0, y1 = (16, H - 6) if H >= 80 else (top + 4, H - 4)
    half = min((W - 20) / 2 if H >= 80 else (W - 10) / 2, (y1 - y0) * (1.7 if H >= 80 else 1.85) / 2)
    x0, x1 = round(W / 2 - half), round(W / 2 + half)
    f = Flat((x0, y0, x1, y1))
    for i in range(12):
        a = round(x0 + (x1 - x0) * i / 12) - 3 if i == 0 else round(x0 + (x1 - x0) * i / 12)
        b = round(x0 + (x1 - x0) * (i + 1) / 12) + (3 if i == 11 else 0)
        d.rectangle([a, top + 1, b - 1, H - 1], fill=(62, 150, 66) if i % 2 == 0 else (52, 134, 58))
    # mower texture: every other row a hair darker
    arr = np.asarray(px).astype(np.int16)
    arr[top + 2::2] -= 4
    px = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8))
    d = ImageDraw.Draw(px)
    white = (238, 242, 236)
    d.rectangle([x0, y0, x1, y1], outline=white)
    d.line([((x0 + x1) // 2, y0), ((x0 + x1) // 2, y1)], fill=white)
    cx, cy = (x0 + x1) // 2, (y0 + y1) // 2
    r = round(9.15 * f.k)
    d.ellipse([cx - r, cy - r, cx + r, cy + r], outline=white)
    px.putpixel((cx, cy), white)
    for side in (0, 1):
        for depth, half in ((16.5, 20.16), (5.5, 9.16)):
            a = f(depth if side == 0 else PITCH_L - depth, PITCH_W / 2 + half)
            b = f(0 if side == 0 else PITCH_L, PITCH_W / 2 - half)
            d.rectangle([round(min(a[0], b[0])), round(a[1]), round(max(a[0], b[0])), round(b[1])], outline=white)
        sx, sy = f(11 if side == 0 else PITCH_L - 11, PITCH_W / 2)
        px.putpixel((round(sx), round(sy)), white)
        # penalty arc
        bx = round(f(16.5 if side == 0 else PITCH_L - 16.5, 0)[0])
        for a in np.linspace(-1.0, 1.0, 40):
            ax = sx + math.cos(a if side == 0 else math.pi - a) * 9.15 * f.k
            ay = sy + math.sin(a) * 9.15 * f.k
            if (side == 0 and ax > bx + 0.5) or (side == 1 and ax < bx - 0.5):
                px.putpixel((round(ax), round(ay)), white)
        # goal with a net
        gx = x0 if side == 0 else x1
        top, bot = round(f(0, PITCH_W / 2 + 3.66)[1]), round(f(0, PITCH_W / 2 - 3.66)[1])
        back = gx - 4 if side == 0 else gx + 4
        for yy in range(top, bot + 1):
            for xx in range(min(gx, back), max(gx, back) + 1):
                if (xx + yy) % 2 == 0:
                    px.putpixel((xx, yy), (200, 210, 205))
        d.line([(gx, top), (back, top)], fill=white)
        d.line([(gx, bot), (back, bot)], fill=white)
        d.line([(back, top), (back, bot)], fill=(210, 214, 210))
    # shadows, then players far-to-near
    holder = min(m.players, key=lambda p: math.hypot(p.x - m.ball[0], p.y - m.ball[1]))
    people = sorted(m.players, key=lambda p: -p.y)
    for p in people:
        sx, sy = f(p.x, p.y)
        for dx in range(-1, 3):
            xx, yy = round(sx) + dx, round(sy) + 1
            if 0 <= xx < W and 0 <= yy < H:
                c = px.getpixel((xx, yy))
                px.putpixel((xx, yy), shade(c, 0.62))
    small = (x1 - x0) < 110
    for p in people:
        sx, sy = f(p.x, p.y)
        pose = int(p.phase * 3) % 3
        if small:
            _sprite(px, round(sx) - 1, round(sy) - 3, p, pose, small=True)
        else:
            _sprite(px, round(sx) - 2, round(sy) - 6, p, pose)
    # the man on the ball gets the classic marker
    hx, hy = f(holder.x, holder.y)
    marks = ((1, 0),) if small else ((2, 1, 0),)
    top = round(hy) - (6 if small else 10)
    for i, w in enumerate(marks[0]):
        for dx in range(-w, w + 1):
            if 0 <= round(hx) + dx < W and 0 <= top + i < H:
                px.putpixel((round(hx) + dx, top + i), (255, 228, 60))
    bx, by = f(m.ball[0], m.ball[1])
    bx, by = round(bx) + 2, round(by)
    px.putpixel((bx + 1, by + 1), (20, 60, 24))
    for dx, dy in ((0, 0), (1, 0), (0, -1), (1, -1)):
        px.putpixel((bx + dx, by + dy), (255, 255, 255) if (dx, dy) != (1, 0) else (190, 190, 190))
    return px


def pixel_retro(m: Match, size=ART) -> Image.Image:
    return term.halfblock(pixel_canvas(m), size)


# ---------------------------------------------------------------- 04 BRAILLE


BRAILLE_BG = (8, 24, 15)


def braille_dots(m: Match, cols: int = 84, rows: int = 30, texture: bool = False,
                 line_col=(150, 220, 175), grass_col=(24, 64, 38)):
    """Colour and priority per dot, (rows*4, cols*2)."""
    DW, DH = cols * 2, rows * 4
    lit = np.zeros((DH, DW))
    col = np.zeros((DH, DW, 3))
    mx, my = 4, 6
    f = Flat((mx, my, DW - 1 - mx, DH - 1 - my))

    def dot(x, y, c, prio):
        x, y = int(round(x)), int(round(y))
        if 0 <= x < DW and 0 <= y < DH and prio >= lit[y, x]:
            lit[y, x] = prio
            col[y, x] = c

    if texture:
        for y in range(DH):
            for x in range(DW):
                if (x % 2 == 0 and y % 2 == 0):
                    dot(x, y, grass_col, 0.1)
    lines, spots = markings()
    for pl in lines:
        pts = [f(x, y) for x, y in pl]
        for (ax, ay), (bx, by) in zip(pts, pts[1:]):
            n = int(max(abs(bx - ax), abs(by - ay)) * 1.5) + 1
            for i in range(n + 1):
                dot(ax + (bx - ax) * i / n, ay + (by - ay) * i / n, line_col, 0.6)
    for x, y, _ in spots:
        sx, sy = f(x, y)
        dot(sx, sy, line_col, 0.6)
    for gx, back in ((0, -2.2), (PITCH_L, PITCH_L + 2.2)):
        for yy in np.linspace(PITCH_W / 2 - 3.66, PITCH_W / 2 + 3.66, 12):
            dot(*f(back, yy), (220, 230, 225), 0.7)
        for xx in np.linspace(min(gx, back), max(gx, back), 4):
            dot(*f(xx, PITCH_W / 2 - 3.66), (220, 230, 225), 0.7)
            dot(*f(xx, PITCH_W / 2 + 3.66), (220, 230, 225), 0.7)
    for p in m.players:
        # snap each man to a 2x2 block of cells so no cell is shared with a neighbour
        cx, cy = f(p.x, p.y)
        cx, cy = round(cx / 2) * 2, round(cy / 4) * 4
        c = vivid(p.kit.shirt, 110)
        for dy in range(-4, 4):
            for dx in range(-2, 2):
                corner = dx in (-2, 1) and dy in (-4, 3)
                if not corner:
                    dot(cx + dx, cy + dy, c, 1.0 + (0.5 if p.keeper else 0))
    bx, by = f(m.ball[0], m.ball[1])
    bx, by = round(bx / 2) * 2 + 2, round(by / 4) * 4
    for dx, dy in ((0, -3), (1, -3), (0, -2), (1, -2), (0, -1), (1, -1)):
        dot(bx + dx, by + dy, (255, 230, 90), 3.0)
    return col, lit


def braille_pitch(m: Match, size=ART, cols: int = 84, rows: int = 30) -> Image.Image:
    col, lit = braille_dots(m, cols, rows)
    return term.braille(col, lit, size, cols, rows, bg=BRAILLE_BG, dot=0.42)


# ---------------------------------------------------------------- 05 ISOMETRIC


def isometric_src(m: Match, size=ART, zoom: float = 1.0, scale: float = 2.2, line_w: float = 0.14,
                  target=(51, 31, 0)) -> Image.Image:
    st = Stage(size, ss=2, bg=(6, 9, 14))
    st.camera(tuple(np.subtract(target, (121, 117, -92))), target, ortho=4.25 * zoom * size[0] / ART[0], shift=(0.0, 0.02))
    look = Look(stands=("far", "near", "left", "right"), sky_top=(6, 9, 14), sky_bottom=(10, 14, 22),
                floodlights=False, grass_a=(70, 150, 62), grass_b=(58, 134, 54), checker=True,
                surround=(48, 112, 46), shadows="sun", crowd_density=1.0, roofs=False, cutaway=True, line_w=line_w)
    stadium(st, m, look, scale=scale)
    return st.finish()


def isometric(m: Match, size=ART) -> Image.Image:
    small = term.pixelate(isometric_src(m, size), (size[0] // 2, size[1] // 2), colours=96)
    return small.resize(size, Image.Resampling.NEAREST)


# ---------------------------------------------------------------- 06 RAYCASTING


def raycast_src(m: Match, size=ART, scale: float = 1.2, line_w: float = 0.18, crowd_calm: float = 0.0,
                eye=(73, 41, 2.1), target=(94, 28, 0.6), fov: float = 44) -> Image.Image:
    st = Stage(size, ss=2, bg=(0, 0, 0))
    st.camera(eye, target, fov=fov)
    look = Look(stands=("far", "left", "right", "near"), sky_top=(10, 14, 34), sky_bottom=(34, 44, 80), grass_a=(60, 156, 62),
                grass_b=(46, 132, 50), line_w=line_w, crowd_light=1.2, exaggerate_ball=1.4, floodlights=False, rise=1.5,
                crowd_calm=crowd_calm)
    stadium(st, m, look, scale=scale)
    return st.finish()


def raycast(m: Match, size=ART) -> Image.Image:
    img = raycast_src(m, size)
    # raycasters draw in columns of fat pixels with a limited palette
    small = term.pixelate(img, (size[0] // 3, round(size[1] / 3 / 2) * 2), colours=48)
    return term.halfblock(small, size, gap=0.12)


# ---------------------------------------------------------------- 07 VOXEL


def voxel(m: Match, size=ART, figure_size: float = 0.5, zoom: float = 1.0) -> Image.Image:
    from .voxel import voxel_scene
    return voxel_scene(m, size, focus=(95.0, 41.0), ortho=10.0 * zoom * size[0] / ART[0], figure_size=figure_size)


# ---------------------------------------------------------------- 08 SPRITE RENDER


def broadcast_camera(st: Stage, m: Match, fov: float = 24.0):
    bx = m.ball[0]
    st.camera((bx - 8, -40, 23), (bx - 5, 33, 0), fov=fov)


def sprite_render(m: Match, size=ART) -> Image.Image:
    st = Stage(size, ss=3)
    st.camera((m.ball[0] - 10, -36, 17), (m.ball[0] - 5, 38, 0), fov=14.5)
    look = Look(stands=("far", "left", "right"), exaggerate_ball=1.6)
    stadium(st, m, look)
    img = st.finish()
    img = noise(img, 2.2, seed=2)
    return vignette(img, 0.25)


# ---------------------------------------------------------------- 09 TACTICAL OVERLAY

HOME_PASSES = [("Ødegaard", "Saka", 8), ("Rice", "Ødegaard", 7), ("Partey", "Rice", 6), ("Ødegaard", "Jesus", 5),
               ("Gabriel", "Partey", 5), ("Saliba", "Rice", 4), ("White", "Ødegaard", 4), ("Rice", "Martinelli", 4),
               ("Zinchenko", "Martinelli", 3), ("Saka", "Jesus", 3), ("Partey", "Ødegaard", 3)]


def heat(m: Match, team: int, shape=(68, 105), sigma=7.0, extra=()):
    yy, xx = np.mgrid[0:shape[0], 0:shape[1]].astype(np.float32)
    xx = xx / shape[1] * PITCH_L
    yy = (1 - yy / shape[0]) * PITCH_W
    h = np.zeros(shape, np.float32)
    pts = [(p.x, p.y, 1.0) for p in m.of(team) if not p.keeper] + list(extra)
    for x, y, w in pts:
        h += w * np.exp(-((xx - x) ** 2 + (yy - y) ** 2) / (2 * sigma ** 2))
    return h / h.max()


def arrow(d, a, b, col, width, head=10.0, shorten=0.0):
    ax, ay = a
    bx, by = b
    ang = math.atan2(by - ay, bx - ax)
    ax, ay = ax + math.cos(ang) * shorten, ay + math.sin(ang) * shorten
    bx, by = bx - math.cos(ang) * shorten, by - math.sin(ang) * shorten
    d.line([(ax, ay), (bx - math.cos(ang) * head * 0.6, by - math.sin(ang) * head * 0.6)], fill=col, width=round(width))
    left = (bx - math.cos(ang - 0.42) * head, by - math.sin(ang - 0.42) * head)
    right = (bx - math.cos(ang + 0.42) * head, by - math.sin(ang + 0.42) * head)
    d.polygon([(bx, by), left, right], fill=col)


def dashed(d, pts, col, width, dash=10, gap=7):
    for (ax, ay), (bx, by) in zip(pts, pts[1:]):
        L = math.hypot(bx - ax, by - ay)
        t = 0.0
        while t < L:
            t1 = min(L, t + dash)
            d.line([(ax + (bx - ax) * t / L, ay + (by - ay) * t / L), (ax + (bx - ax) * t1 / L, ay + (by - ay) * t1 / L)], fill=col, width=round(width))
            t += dash + gap


def token(d, x, y, r, p, ss, ring=None):
    col = p.kit.shirt
    if ring:
        d.ellipse([x - r * 1.55, y - r * 1.55, x + r * 1.55, y + r * 1.55], outline=ring, width=round(1.4 * ss))
    d.ellipse([x - r, y - r, x + r, y + r], fill=col, outline=(255, 255, 255), width=round(1.1 * ss))
    num_col = p.kit.number if lum(col) < 170 else (20, 20, 20)
    d.text((x, y + r * 0.06), str(p.number), font=font(round(r * 1.05), "Bold"), fill=num_col, anchor="mm")


def tactical(m: Match, size=ART) -> Image.Image:
    W, H = size
    ss = 3
    legend_w = 128

    def draw(img, s):
        d = ImageDraw.Draw(img, "RGBA")
        img.paste((12, 18, 24), [0, 0, img.width, img.height])
        box = pitch_box(((W - legend_w) * s, H * s), 10 * s, 14 * s)
        f = Flat(box)
        x0, y0, x1, y1 = box
        for i in range(12):
            a = x0 + (x1 - x0) * i / 12
            b = x0 + (x1 - x0) * (i + 1) / 12
            d.rectangle([a, y0, b, y1], fill=(28, 74, 44) if i % 2 == 0 else (24, 66, 40))
        # heat maps, red for us, blue for them
        hh = heat(m, 0, extra=[(70, 20, 1.2), (82, 30, 1.4), (78, 50, 0.8)])
        ha = heat(m, 1, extra=[(92, 34, 1.4), (85, 50, 0.8)])
        ph, pw = round(y1 - y0), round(x1 - x0)
        hh = np.asarray(Image.fromarray((hh * 255).astype(np.uint8)).resize((pw, ph), Image.Resampling.BICUBIC)) / 255
        ha = np.asarray(Image.fromarray((ha * 255).astype(np.uint8)).resize((pw, ph), Image.Resampling.BICUBIC)) / 255
        rgba = np.zeros((ph, pw, 4), np.float32)
        red, blue = np.array((240, 70, 60), np.float32), np.array((60, 130, 250), np.float32)
        wsum = hh[..., None] * red + ha[..., None] * blue
        tot = np.clip(hh + ha, 1e-4, None)[..., None]
        rgba[..., :3] = wsum / tot
        rgba[..., 3] = np.clip(np.maximum(hh, ha) ** 1.25 * 205, 0, 190)
        layer = Image.fromarray(rgba.astype(np.uint8), "RGBA")
        img.paste(layer, (round(x0), round(y0)), layer)
        d = ImageDraw.Draw(img, "RGBA")
        flat_lines(d, f, (235, 245, 238, 150), 1.3 * s)
        # their defensive line
        back4 = [p for p in m.of(1) if not p.keeper and p.x > 80]
        line_x = sum(p.x for p in back4) / len(back4)
        dashed(d, [f(line_x, 2), f(line_x, PITCH_W - 2)], (120, 180, 255, 220), 1.6 * s, 8 * s, 6 * s)
        lx, ly = f(line_x, PITCH_W - 2)
        d.text((lx - 4 * s, ly + 2 * s), "DEF LINE", font=font(round(7.5 * s), "Bold"), fill=(140, 190, 255), anchor="ra")
        # pressing zone around the ball
        press = [m.by_name(n) for n in ("Saka", "Jesus", "Ødegaard", "Martinelli")]
        hull = [f(p.x, p.y) for p in press]
        cx = sum(p[0] for p in hull) / len(hull)
        cy = sum(p[1] for p in hull) / len(hull)
        hull.sort(key=lambda p: math.atan2(p[1] - cy, p[0] - cx))
        grown = [(cx + (px - cx) * 1.25, cy + (py - cy) * 1.25) for px, py in hull]
        d.polygon(grown, fill=(255, 180, 60, 46))
        dashed(d, grown + [grown[0]], (255, 190, 80, 230), 1.6 * s, 7 * s, 5 * s)
        # passes
        for a, b, n in HOME_PASSES:
            pa, pb = m.by_name(a), m.by_name(b)
            arrow(d, f(pa.x, pa.y), f(pb.x, pb.y), (255, 226, 92, 120 + n * 14), (0.5 + n * 0.32) * s, head=7 * s, shorten=8 * s)
        # runs: Saka's run in behind
        saka = m.by_name("Saka")
        dashed(d, [f(saka.x, saka.y), f(saka.x + 12, saka.y + 6)], (255, 255, 255, 220), 1.4 * s, 5 * s, 4 * s)
        arrow(d, f(saka.x + 10, saka.y + 5), f(saka.x + 13, saka.y + 6.5), (255, 255, 255, 230), 1.4 * s, head=6 * s)
        for p in sorted(m.players, key=lambda p: p.team):
            x, y = f(p.x, p.y)
            token(d, x, y, 6.4 * s, p, s)
        bx, by = f(m.ball[0], m.ball[1])
        d.ellipse([bx - 3 * s, by - 3 * s, bx + 3 * s, by + 3 * s], fill=(255, 255, 255), outline=(0, 0, 0), width=s)
        # legend
        lx0 = (W - legend_w + 4) * s
        d.rounded_rectangle([lx0, 14 * s, (W - 8) * s, (H - 14) * s], radius=6 * s, fill=(16, 23, 31), outline=(40, 54, 68), width=s)
        d.text((lx0 + 10 * s, 24 * s), "OVERLAYS", font=font(round(8.5 * s), "Bold"), fill=(150, 168, 186))
        items = [((240, 80, 70), "Heatmap ARS"), ((70, 140, 250), "Heatmap CHE"), ((255, 226, 92), "Passes"),
                 ((255, 190, 80), "Pressing"), ((255, 255, 255), "Player runs"), ((120, 180, 255), "Def. line")]
        for i, (c, label) in enumerate(items):
            yy = (44 + i * 19) * s
            d.rounded_rectangle([lx0 + 10 * s, yy, lx0 + 20 * s, yy + 10 * s], radius=2 * s, fill=c)
            d.text((lx0 + 27 * s, yy + 5 * s), label, font=font(round(8.5 * s)), fill=(214, 224, 234), anchor="lm")
        # zone possession bars
        d.text((lx0 + 10 * s, 168 * s), "ZONE CONTROL", font=font(round(8 * s), "Bold"), fill=(150, 168, 186))
        zones = [(0.38, 0.62), (0.55, 0.45), (0.71, 0.29)]
        for i, (a, b) in enumerate(zones):
            yy = (186 + i * 30) * s
            d.text((lx0 + 10 * s, yy), ("DEF", "MID", "ATT")[i], font=font(round(7.5 * s)), fill=(130, 148, 166))
            bw = (legend_w - 30) * s
            d.rectangle([lx0 + 10 * s, yy + 11 * s, lx0 + 10 * s + bw * a, yy + 18 * s], fill=(230, 70, 64))
            d.rectangle([lx0 + 10 * s + bw * a, yy + 11 * s, lx0 + 10 * s + bw, yy + 18 * s], fill=(60, 130, 240))
            d.text((lx0 + 10 * s + bw, yy), f"{round(a * 100)}%", font=font(round(7.5 * s), "Bold"), fill=(240, 120, 110), anchor="ra")
        # mini bars: press intensity per 5 minutes
        d.text((lx0 + 10 * s, 282 * s), "PPDA / 5'", font=font(round(8 * s), "Bold"), fill=(150, 168, 186))
        vals = [5, 7, 6, 9, 8, 11, 12, 10, 13, 14]
        for i, v in enumerate(vals):
            bxx = lx0 + (10 + i * 10.5) * s
            d.rectangle([bxx, (330 - v * 2.4) * s, bxx + 7 * s, 330 * s], fill=(255, 190, 80) if i >= 7 else (90, 110, 130))

    return supersample(draw, size, ss)


# ---------------------------------------------------------------- 10 CINEMATIC REPLAY


def goal_moment(m: Match) -> Match:
    """The finish a frame later: ball in the side netting, keeper at full stretch."""
    from dataclasses import replace
    players = []
    for p in m.players:
        if p.keeper and p.team == 1:
            p = replace(p, x=102.2, y=33.4, lean=-66, lift=0.6, facing=200)
        players.append(p)
    return replace(m, players=players, ball=(104.2, 31.0, 1.9))


def film_grade(img: Image.Image, tint=(1.0, 1.02, 1.08), contrast=1.25, grain=7.0, seed=3) -> Image.Image:
    g = np.asarray(img.convert("L")).astype(np.float32) / 255
    g = np.clip((g - 0.5) * contrast + 0.5, 0, 1)
    g = g ** 0.95
    rgb = np.stack([g * tint[0], g * tint[1], g * tint[2]], -1) * 255
    rng = np.random.default_rng(seed)
    rgb += rng.normal(0, grain, g.shape)[..., None]
    return Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8))


def cinematic(m: Match, size=ART) -> Image.Image:
    W, H = size
    m = goal_moment(m)
    st = Stage(size, ss=3)
    st.camera((111, 38.5, 2.0), (90, 29.5, 1.5), fov=44)
    stadium(st, m, Look(stands=("far", "near", "left"), exaggerate_ball=1.3))
    img = st.finish()
    # depth of field: soften the stands, keep the goalmouth sharp
    blurred = img.filter(ImageFilter.GaussianBlur(2.6))
    mask = Image.new("L", size, 0)
    md = ImageDraw.Draw(mask)
    for y in range(H):
        t = max(0.0, min(1.0, (0.5 - y / H) / 0.22))
        md.line([(0, y), (W, y)], fill=round(255 * t))
    img = Image.composite(blurred, img, mask)
    img = film_grade(img)
    img = vignette(img, 0.55)
    d = ImageDraw.Draw(img, "RGBA")
    bar = round(H * 0.085)
    for i in range(70):
        y = H - bar - 70 + i
        d.line([(0, y), (W, y)], fill=(0, 0, 0, round(150 * i / 70)))
    d.rectangle([0, 0, W, bar], fill=(0, 0, 0))
    d.rectangle([0, H - bar, W, H], fill=(0, 0, 0))
    d.ellipse([14, bar + 12, 24, bar + 22], fill=(235, 40, 40))
    d.text((32, bar + 17), "REPLAY", font=font(15, "Heavy"), fill=(235, 40, 40), anchor="lm")
    d.text((W - 14, bar + 10), "1/4×  SLOW", font=font(11, "Semibold"), fill=(230, 230, 230), anchor="ra")
    d.text((W - 14, bar + 26), "CAM 4 · BEHIND GOAL", font=font(9), fill=(170, 170, 170), anchor="ra")
    d.text((14, H - bar - 30), "67:14", font=font(20, "Bold"), fill=(255, 255, 255), anchor="ls")
    d.text((78, H - bar - 31), "ØDEGAARD → SAKA → JESUS", font=font(11, "Semibold"), fill=(220, 220, 220), anchor="ls")
    # scrubber
    y = H - bar - 14
    d.rectangle([14, y, W - 14, y + 3], fill=(255, 255, 255, 70))
    d.rectangle([14, y, 14 + (W - 28) * 0.72, y + 3], fill=(235, 40, 40))
    d.ellipse([14 + (W - 28) * 0.72 - 5, y - 4, 14 + (W - 28) * 0.72 + 5, y + 7], fill=(255, 255, 255))
    d.text((14, H - bar / 2), "00:03.12", font=font(9), fill=(150, 150, 150), anchor="lm")
    d.text((W - 14, H - bar / 2), "00:04.30", font=font(9), fill=(150, 150, 150), anchor="rm")
    return img


# ---------------------------------------------------------------- 11 BROADCAST UI


def avatar(d: ImageDraw.ImageDraw, cx, cy, r, p, bg):
    """A head-and-shoulders portrait in a circle."""
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=bg)
    kit = p.kit
    # shoulders
    d.chord([cx - r * 1.05, cy + r * 0.38, cx + r * 1.05, cy + r * 2.0], 180, 360, fill=kit.shirt)
    d.polygon([(cx - r * 0.22, cy + r * 0.42), (cx + r * 0.22, cy + r * 0.42), (cx, cy + r * 0.72)], fill=shade(kit.shirt, 0.6))
    if kit.sleeves != kit.shirt:
        d.pieslice([cx - r * 1.05, cy + r * 0.38, cx + r * 1.05, cy + r * 2.0], 180, 205, fill=kit.sleeves)
        d.pieslice([cx - r * 1.05, cy + r * 0.38, cx + r * 1.05, cy + r * 2.0], 335, 360, fill=kit.sleeves)
    # neck and face
    d.rectangle([cx - r * 0.17, cy + r * 0.05, cx + r * 0.17, cy + r * 0.48], fill=shade(p.skin, 0.85))
    d.ellipse([cx - r * 0.36, cy - r * 0.58, cx + r * 0.36, cy + r * 0.3], fill=p.skin)
    d.ellipse([cx - r * 0.44, cy - r * 0.2, cx - r * 0.3, cy + r * 0.0], fill=shade(p.skin, 0.9))
    d.ellipse([cx + r * 0.3, cy - r * 0.2, cx + r * 0.44, cy + r * 0.0], fill=shade(p.skin, 0.9))
    d.chord([cx - r * 0.39, cy - r * 0.68, cx + r * 0.39, cy - r * 0.02], 180, 360, fill=p.hair)
    d.rectangle([cx - r * 0.39, cy - r * 0.36, cx - r * 0.32, cy - r * 0.12], fill=p.hair)
    d.rectangle([cx + r * 0.32, cy - r * 0.36, cx + r * 0.39, cy - r * 0.12], fill=p.hair)
    eye = (30, 26, 24)
    for sx in (-1, 1):
        d.ellipse([cx + sx * r * 0.15 - r * 0.045, cy - r * 0.17, cx + sx * r * 0.15 + r * 0.045, cy - r * 0.09], fill=eye)
        d.line([(cx + sx * r * 0.08, cy - r * 0.25), (cx + sx * r * 0.23, cy - r * 0.26)], fill=shade(p.hair, 1.0), width=max(1, round(r * 0.04)))
    d.line([(cx - r * 0.1, cy + r * 0.1), (cx + r * 0.1, cy + r * 0.1)], fill=shade(p.skin, 0.6), width=max(1, round(r * 0.04)))
    d.line([(cx, cy - r * 0.1), (cx - r * 0.03, cy + r * 0.02)], fill=shade(p.skin, 0.78), width=max(1, round(r * 0.035)))


def scorebug(d, x, y, m: Match, s=1.0):
    """A broadcaster's score bug: crest blocks, codes, score and clock."""
    h = 22 * s
    f = font(round(12 * s), "Bold")
    fc = font(round(12 * s), "Semibold")
    w_code = 44 * s
    d.rounded_rectangle([x, y, x + 210 * s, y + h], radius=4 * s, fill=(10, 14, 22, 235))
    d.rectangle([x, y, x + 5 * s, y + h], fill=m.home.colour)
    d.text((x + 10 * s, y + h / 2), m.home.code, font=fc, fill=(255, 255, 255), anchor="lm")
    d.rectangle([x + w_code + 6 * s, y, x + w_code + 54 * s, y + h], fill=(240, 240, 244))
    d.text((x + w_code + 30 * s, y + h / 2), f"{m.score[0]} - {m.score[1]}", font=f, fill=(16, 18, 26), anchor="mm")
    d.text((x + w_code + 60 * s, y + h / 2), m.away.code, font=fc, fill=(255, 255, 255), anchor="lm")
    d.rectangle([x + w_code + 94 * s, y, x + w_code + 99 * s, y + h], fill=m.away.colour)
    d.text((x + 200 * s, y + h / 2), m.clock, font=fc, fill=(140, 230, 170), anchor="rm")


def broadcast(m: Match, size=ART) -> Image.Image:
    W, H = size
    vid_w, vid_h = 372, 236
    st = Stage((vid_w, vid_h), ss=3)
    st.camera((m.ball[0] - 9, -40, 22), (m.ball[0] - 5, 36, 0), fov=19)
    stadium(st, m, Look(stands=("far", "left", "right"), exaggerate_ball=1.8))
    vid = vignette(noise(st.finish(), 1.8), 0.15)
    ss = 3

    def draw(img, s):
        d = ImageDraw.Draw(img, "RGBA")
        img.paste((10, 14, 20), [0, 0, img.width, img.height])
        img.paste(vid.resize((vid_w * s, vid_h * s), Image.Resampling.LANCZOS), (0, 0))
        d = ImageDraw.Draw(img, "RGBA")
        scorebug(d, 10 * s, 10 * s, m, s)
        d.rounded_rectangle([(vid_w - 52) * s, 10 * s, (vid_w - 10) * s, 26 * s], radius=3 * s, fill=(220, 30, 40))
        d.text(((vid_w - 31) * s, 18 * s), "LIVE", font=font(round(9 * s), "Heavy"), fill=(255, 255, 255), anchor="mm")
        # stats panel
        px0 = (vid_w + 10) * s
        d.text((px0, 6 * s), "MATCH STATS", font=font(round(10.5 * s), "Bold"), fill=(230, 236, 242))
        d.line([(px0, 22 * s), ((W - 4) * s, 22 * s)], fill=(44, 58, 72), width=s)
        stats = m.meta["stats"]
        rows = [("Possession", stats["possession"], "{}%"), ("Shots", stats["shots"], "{}"), ("On target", stats["on_target"], "{}"),
                ("xG", stats["xg"], "{:.2f}"), ("Corners", stats["corners"], "{}"), ("Fouls", stats["fouls"], "{}")]
        for i, (label, (a, b), fmt) in enumerate(rows):
            y = (32 + i * 34) * s
            d.text((px0, y), fmt.format(a), font=font(round(10.5 * s), "Bold"), fill=(255, 130, 120))
            d.text(((W - 4) * s, y), fmt.format(b), font=font(round(10.5 * s), "Bold"), fill=(120, 170, 255), anchor="ra")
            d.text(((px0 + (W - 4) * s) / 2, y + 1 * s), label, font=font(round(8.5 * s)), fill=(150, 166, 184), anchor="ma")
            bw = (W - 4) * s - px0
            t = a / (a + b) if (a + b) else 0.5
            d.rounded_rectangle([px0, y + 15 * s, px0 + bw * t - s, y + 20 * s], radius=2 * s, fill=(225, 50, 50))
            d.rounded_rectangle([px0 + bw * t + s, y + 15 * s, px0 + bw, y + 20 * s], radius=2 * s, fill=(40, 110, 220))
        # player cards
        cards = m.meta["player_cards"]
        cw = (W - 8) / 2
        for i, card in enumerate(cards):
            p = m.by_name(card["name"])
            x0 = (i * (cw + 8)) * s
            y0 = (vid_h + 8) * s
            x1 = x0 + cw * s
            y1 = (H - 30) * s
            team = m.team(p.team)
            d.rounded_rectangle([x0, y0, x1, y1], radius=6 * s, fill=(16, 22, 30), outline=(40, 52, 66), width=s)
            d.rectangle([x0, y0 + 6 * s, x0 + 4 * s, y1 - 6 * s], fill=team.colour)
            r = 26 * s
            cx, cy = x0 + 40 * s, (y0 + y1) / 2
            avatar(d, cx, cy, r, p, shade(team.colour, 0.45))
            d.ellipse([cx + r * 0.45, cy + r * 0.4, cx + r * 1.05, cy + r * 1.0], fill=team.colour, outline=(255, 255, 255), width=s)
            d.text((cx + r * 0.75, cy + r * 0.72), str(p.number), font=font(round(8.5 * s), "Bold"), fill=(255, 255, 255), anchor="mm")
            tx = x0 + 78 * s
            d.text((tx, y0 + 12 * s), p.name.upper(), font=font(round(12 * s), "Bold"), fill=(240, 244, 248))
            d.text((tx, y0 + 30 * s), f"{team.name}", font=font(round(8.5 * s)), fill=(140, 156, 174))
            if "key_passes" in card:
                numbers = [("KEY PASSES", str(card["key_passes"])), ("xA", f"{card['xa']:.1f}")]
            else:
                numbers = [("SHOTS", str(card["shots"])), ("xG", f"{card['xg']:.1f}")]
            for j, (k, v) in enumerate(numbers):
                sx = tx + j * 72 * s
                d.text((sx, y0 + 50 * s), v, font=font(round(14 * s), "Bold"), fill=(255, 255, 255))
                d.text((sx, y0 + 68 * s), k, font=font(round(7.5 * s)), fill=(130, 146, 164))
            rating = card["rating"]
            d.rounded_rectangle([x1 - 40 * s, y0 + 10 * s, x1 - 10 * s, y0 + 26 * s], radius=3 * s, fill=(30, 150, 90) if rating >= 7.5 else (190, 150, 40))
            d.text((x1 - 25 * s, y0 + 18 * s), f"{rating:.1f}", font=font(round(9.5 * s), "Bold"), fill=(255, 255, 255), anchor="mm")
        # ticker
        ty = (H - 22) * s
        d.rectangle([0, ty, W * s, H * s], fill=(18, 24, 34))
        d.rectangle([0, ty, 58 * s, H * s], fill=(220, 30, 40))
        d.text((29 * s, ty + 11 * s), "67'", font=font(round(10 * s), "Heavy"), fill=(255, 255, 255), anchor="mm")
        d.text((66 * s, ty + 11 * s), m.commentary, font=font(round(9.5 * s), "Semibold"), fill=(226, 232, 240), anchor="lm")

    return supersample(draw, size, ss)


# ---------------------------------------------------------------- 12 PARTICLES


DOT_FONT = {
    "G": [".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".###."],
    "O": [".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
    "A": [".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"],
    "L": ["#....", "#....", "#....", "#....", "#....", "#....", "#####"],
    "!": [".#.", ".#.", ".#.", ".#.", ".#.", "...", ".#."],
}


def led_text(text: str) -> np.ndarray:
    """A word in a 5x7 dot-matrix font, as a boolean grid."""
    cols = []
    for i, ch in enumerate(text):
        glyph = DOT_FONT[ch]
        if i:
            cols.append(np.zeros((7, 1), bool))
        cols.append(np.array([[c == "#" for c in row] for row in glyph]))
    return np.concatenate(cols, axis=1)


def particles(m: Match, size=ART) -> Image.Image:
    W, H = size
    m = goal_moment(m)
    st = Stage(size, ss=2)
    st.camera((52, -62, 30), (70, 40, 4), fov=40)
    stadium(st, m, Look(stands=("far", "left", "right"), sky_top=(2, 3, 10), sky_bottom=(10, 12, 26), crowd_light=1.25))
    base = np.asarray(st.finish()).astype(np.float32) * 0.42
    layer = np.zeros_like(base)
    pil = Image.fromarray(layer.astype(np.uint8))
    d = ImageDraw.Draw(pil)
    rng = np.random.default_rng(12)
    bursts = [((70, 52), (255, 70, 60), 44), ((492, 46), (255, 210, 90), 46), ((282, 26), (255, 255, 255), 30),
              ((178, 22), (255, 150, 60), 24), ((392, 24), (255, 90, 80), 26)]
    for (cx, cy), col, R in bursts:
        n = 56
        for k in range(n):
            a = 2 * math.pi * k / n + rng.uniform(-0.04, 0.04)
            r = R * rng.uniform(0.75, 1.05)
            ex, ey = cx + math.cos(a) * r, cy + math.sin(a) * r + r * 0.12
            for t in np.linspace(0.35, 1.0, 7):
                px, py = cx + (ex - cx) * t, cy + (ey - cy) * t + (t ** 2) * 6
                c = tuple(round(v * (0.25 + 0.75 * t)) for v in col)
                rr = 0.5 + 1.2 * t
                d.ellipse([px - rr, py - rr, px + rr, py + rr], fill=c)
        d.ellipse([cx - 3, cy - 3, cx + 3, cy + 3], fill=(255, 255, 255))
    # confetti over the pitch
    for _ in range(110):
        x, y = rng.uniform(0, W), rng.uniform(H * 0.6, H)
        c = [(255, 60, 60), (255, 220, 80), (255, 255, 255), (230, 40, 40)][rng.integers(0, 4)]
        a = rng.uniform(0, math.pi)
        L = rng.uniform(1.5, 3.5)
        d.line([(x - math.cos(a) * L, y - math.sin(a) * L), (x + math.cos(a) * L, y + math.sin(a) * L)], fill=c, width=2)
    # GOAL! in LED dots
    mask = led_text("GOAL!")
    rows_, cols_ = mask.shape
    pitch = min((W * 0.74) / cols_, (H * 0.34) / rows_)
    ox = (W - cols_ * pitch) / 2
    oy = H * 0.41 - rows_ * pitch / 2
    for r in range(rows_):
        for c in range(cols_):
            x, y = ox + (c + 0.5) * pitch, oy + (r + 0.5) * pitch
            rr = pitch * 0.4
            if mask[r, c]:
                t = r / rows_
                col = mix((255, 236, 140), (255, 150, 40), t)
                d.ellipse([x - rr, y - rr, x + rr, y + rr], fill=col)
            else:
                d.ellipse([x - rr * 0.22, y - rr * 0.22, x + rr * 0.22, y + rr * 0.22], fill=(18, 12, 5))
    layer = np.asarray(pil).astype(np.float32)
    bloom = np.asarray(Image.fromarray(layer.astype(np.uint8)).filter(ImageFilter.GaussianBlur(7))).astype(np.float32)
    bloom2 = np.asarray(Image.fromarray(layer.astype(np.uint8)).filter(ImageFilter.GaussianBlur(22))).astype(np.float32)
    out = np.where(layer.max(axis=2, keepdims=True) > 0, layer, base) + bloom * 0.55 + bloom2 * 0.4
    img = Image.fromarray(np.clip(out, 0, 255).astype(np.uint8))
    d = ImageDraw.Draw(img, "RGBA")
    ty = oy + rows_ * pitch + 14
    name = "ØDEGAARD  67'"
    d.rounded_rectangle([W / 2 - 110, ty, W / 2 + 110, ty + 30], radius=4, fill=(0, 0, 0, 170))
    d.text((W / 2, ty + 15), name, font=font(17, "Heavy"), fill=(255, 255, 255), anchor="mm")
    d.rounded_rectangle([W / 2 - 62, ty + 36, W / 2 + 62, ty + 56], radius=4, fill=(0, 0, 0, 150))
    d.text((W / 2, ty + 46), f"{m.home.code} 3 - 1 {m.away.code}", font=font(13, "Bold"), fill=(255, 220, 120), anchor="mm")
    return img


# ---------------------------------------------------------------- 13 ANALYTICS

SHOTS = [  # minute, team, x, y, xg, goal
    (8, 0, 88, 40, 0.08, False), (23, 0, 95, 28, 0.62, True), (31, 0, 80, 30, 0.05, False), (38, 0, 92, 44, 0.18, False),
    (44, 0, 86, 22, 0.07, False), (52, 0, 98, 36, 0.35, False), (61, 0, 84, 50, 0.04, False), (67, 0, 97, 33, 0.45, True),
    (12, 1, 85, 36, 0.08, False), (29, 1, 90, 26, 0.21, False), (58, 1, 94, 38, 0.55, True), (63, 1, 82, 42, 0.08, False),
]


def panel(d, box, title, s, right=None):
    x0, y0, x1, y1 = box
    d.rounded_rectangle([x0, y0, x1, y1], radius=5 * s, fill=(14, 20, 27), outline=(36, 48, 62), width=s)
    d.text((x0 + 8 * s, y0 + 6 * s), title, font=font(round(8 * s), "Bold"), fill=(150, 168, 186))
    if right:
        d.text((x1 - 8 * s, y0 + 6 * s), right, font=font(round(7.5 * s)), fill=(110, 126, 144), anchor="ra")


def analytics(m: Match, size=ART) -> Image.Image:
    W, H = size

    def draw(img, s):
        d = ImageDraw.Draw(img, "RGBA")
        img.paste((9, 13, 18), [0, 0, img.width, img.height])
        gap = 6 * s
        mid_x, mid_y = W * s * 0.52, H * s * 0.5
        A = (0, 0, mid_x - gap / 2, mid_y - gap / 2)
        B = (mid_x + gap / 2, 0, W * s - 1, mid_y - gap / 2)
        C = (0, mid_y + gap / 2, mid_x - gap / 2, H * s - 1)
        D = (mid_x + gap / 2, mid_y + gap / 2, W * s - 1, H * s - 1)

        # A: pass network
        panel(d, A, "PASS NETWORK · ARS", s, "avg. positions")
        box = pitch_box((A[2] - A[0], A[3] - A[1] - 18 * s), 8 * s, 4 * s)
        f = Flat((A[0] + box[0], A[1] + 18 * s + box[1], A[0] + box[2], A[1] + 18 * s + box[3]))
        d.rectangle([f.x0, f.y0, f.x1, f.y1], fill=(18, 30, 26))
        flat_lines(d, f, (90, 120, 108), 0.8 * s, goals=False)
        touches = {}
        for a, b, n in HOME_PASSES:
            touches[a] = touches.get(a, 0) + n
            touches[b] = touches.get(b, 0) + n
            pa, pb = m.by_name(a), m.by_name(b)
            d.line([f(pa.x, pa.y), f(pb.x, pb.y)], fill=(255, 200, 90, 70 + n * 20), width=max(1, round((0.4 + n * 0.35) * s)))
        for p in m.of(0):
            x, y = f(p.x, p.y)
            r = (3.2 + touches.get(p.name, 2) * 0.32) * s
            d.ellipse([x - r, y - r, x + r, y + r], fill=(225, 40, 50), outline=(255, 255, 255), width=s)
            d.text((x, y), str(p.number), font=font(round(r * 0.95), "Bold"), fill=(255, 255, 255), anchor="mm")

        # B: xG race
        panel(d, B, "xG TIMELINE", s)
        gx0, gy0, gx1, gy1 = B[0] + 24 * s, B[1] + 26 * s, B[2] - 12 * s, B[3] - 18 * s
        for v in (0.5, 1.0, 1.5, 2.0):
            y = gy1 - (gy1 - gy0) * v / 2.0
            d.line([(gx0, y), (gx1, y)], fill=(32, 42, 54), width=s)
            d.text((gx0 - 4 * s, y), f"{v:.1f}", font=font(round(6.5 * s)), fill=(100, 116, 134), anchor="rm")
        for mnt in (0, 45, 90):
            x = gx0 + (gx1 - gx0) * mnt / 90
            d.text((x, gy1 + 4 * s), f"{mnt}'", font=font(round(6.5 * s)), fill=(100, 116, 134), anchor="ma")
        d.line([(gx0 + (gx1 - gx0) * 0.5, gy0), (gx0 + (gx1 - gx0) * 0.5, gy1)], fill=(44, 56, 70), width=s)
        for team, col in ((0, (240, 70, 70)), (1, (80, 150, 255))):
            pts = [(gx0, gy1)]
            tot = 0.0
            for mnt, t, x, y, xg, g in sorted(SHOTS):
                if t != team:
                    continue
                X = gx0 + (gx1 - gx0) * mnt / 90
                pts.append((X, gy1 - (gy1 - gy0) * tot / 2.0))
                tot += xg
                pts.append((X, gy1 - (gy1 - gy0) * tot / 2.0))
                if g:
                    d.ellipse([X - 3.5 * s, pts[-1][1] - 3.5 * s, X + 3.5 * s, pts[-1][1] + 3.5 * s], fill=col, outline=(255, 255, 255), width=s)
            X = gx0 + (gx1 - gx0) * 67 / 90
            pts.append((X, pts[-1][1]))
            d.line(pts, fill=col, width=round(1.8 * s), joint="curve")
            d.text((X + 5 * s, pts[-1][1]), f"{tot:.2f}", font=font(round(7.5 * s), "Bold"), fill=col, anchor="lm")
        X = gx0 + (gx1 - gx0) * 67 / 90
        dashed(d, [(X, gy0), (X, gy1)], (255, 255, 255, 120), s, 3 * s, 3 * s)

        # C: shot map (attacking half, both teams folded onto it)
        panel(d, C, "SHOT MAP", s, "● goal  ○ miss")
        cx0, cy0, cx1, cy1 = C[0] + 10 * s, C[1] + 22 * s, C[2] - 10 * s, C[3] - 8 * s
        half = Flat((cx0 - (cx1 - cx0), cy0, cx1, cy1))  # only the right half is on screen
        d.rectangle([cx0, cy0, cx1, cy1], fill=(18, 30, 26))
        lines, spots = markings()
        for pl in lines:
            pts = [half(x, y) for x, y in pl if x >= PITCH_L / 2 - 0.01]
            if len(pts) > 1:
                d.line(pts, fill=(90, 120, 108), width=max(1, round(0.8 * s)))
        for mnt, t, x, y, xg, g in SHOTS:
            px, py = half(x, y if t == 0 else PITCH_W - y)
            r = (2.5 + xg * 10) * s
            col = (240, 70, 70) if t == 0 else (80, 150, 255)
            if g:
                d.ellipse([px - r, py - r, px + r, py + r], fill=col + (230,), outline=(255, 255, 255), width=s)
            else:
                d.ellipse([px - r, py - r, px + r, py + r], outline=col + (220,), width=max(1, round(1.2 * s)))

        # D: momentum
        panel(d, D, "MOMENTUM", s, "per 5'")
        mx0, my0, mx1, my1 = D[0] + 10 * s, D[1] + 24 * s, D[2] - 10 * s, D[3] - 12 * s
        mid = (my0 + my1) / 2
        d.line([(mx0, mid), (mx1, mid)], fill=(50, 62, 76), width=s)
        vals = [0.2, 0.5, -0.3, 0.6, 0.8, 0.3, -0.2, 0.1, -0.5, -0.8, -0.3, 0.4, 0.7, 0.9]
        bw = (mx1 - mx0) / len(vals)
        for i, v in enumerate(vals):
            x = mx0 + i * bw
            y = mid - v * (my1 - my0) / 2 * 0.9
            col = (240, 70, 70) if v > 0 else (80, 150, 255)
            d.rounded_rectangle([x + bw * 0.15, min(mid, y), x + bw * 0.85, max(mid, y)], radius=round(1.5 * s), fill=col)
        for mnt, team in ((23, 0), (58, 1), (67, 0)):
            x = mx0 + (mx1 - mx0) * mnt / 70
            y = my0 + 1 * s if team == 0 else my1 - 1 * s
            col = (240, 70, 70) if team == 0 else (80, 150, 255)
            d.ellipse([x - 3 * s, y - 3 * s, x + 3 * s, y + 3 * s], fill=(255, 255, 255), outline=col, width=round(1.5 * s))
            d.text((x + 5 * s, y), f"{mnt}'", font=font(round(6.5 * s), "Bold"), fill=(200, 210, 220), anchor="lm")

    return supersample(draw, size, 3)
