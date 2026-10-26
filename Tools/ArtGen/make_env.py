"""The room: walls, floors, window, lift shaft backdrop. Run from the repo root:
    Tools/ArtGen/.venv/bin/python Tools/ArtGen/make_env.py
"""
import os
import random

from lowpoly import *

OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "Art", "Generated")
os.makedirs(OUT, exist_ok=True)

SLATE = hexc("#2c3544")
STEEL = hexc("#46556a")
CONCRETE = hexc("#575d66")
RUST = hexc("#a8532c")
HAZARD = hexc("#e8b923")


def wall():
    """The back wall of the whole room, 32 x 18 units."""
    W, H = 32 * PPU, 18 * PPU
    c = Canvas(W, H)
    facets(c, 0, 0, W, H, SLATE, cell=64, amp=.055, seed=3, light=(0, -.6))
    # Tall plaster panels with seams, riveted along the edges.
    for i, x in enumerate(range(0, W + 1, 2 * PPU)):
        c.rect(x - 2, 0, x + 1, H, (0, 0, 0, 80))
        c.rect(x + 1, 0, x + 3, H, (255, 255, 255, 14))
        for y in range(40, H, 150):
            rivet(c, x + 10, y, 2.0, SLATE)
            rivet(c, x - 10, y, 2.0, SLATE)
    # A darker wainscot at the bottom, with a steel trim along its top.
    wain_top = H - 5 * PPU
    facets(c, 0, wain_top, W, H, mul(SLATE, .78), cell=48, amp=.07, seed=11, light=(0, -.3))
    for x in range(0, W + 1, 1 * PPU):
        c.rect(x - 1, wain_top, x + 1, H, (0, 0, 0, 60))
    c.rect(0, wain_top - 5, W, wain_top + 7, mul(STEEL, 1.05))
    c.rect(0, wain_top - 5, W, wain_top - 2, (255, 255, 255, 55))
    c.rect(0, wain_top + 5, W, wain_top + 9, (0, 0, 0, 90))
    # Stains and damp, blotches of darker facets climbing from the floor.
    rnd = random.Random(5)
    for _ in range(26):
        x = rnd.randint(0, W)
        y = rnd.randint(int(H * .35), H - 128)
        w = rnd.randint(40, 120)
        h = rnd.randint(30, 110)
        pts = [(x, y), (x + w * .5, y - h * .3), (x + w, y + h * .1), (x + w * .6, y + h), (x + w * .1, y + h * .7)]
        c.poly(pts, (0, 0, 0, rnd.randint(14, 34)))
    # The ceiling end is dark, the middle a little lighter.
    for s in range(60):
        t = s / 60
        c.rect(0, t * H * .35, W, (t + 1 / 60) * H * .35 + 1, (6, 8, 14, int(150 * (1 - t) ** 1.6)))
    c.save(os.path.join(OUT, "wall_back.png"))


def floor_lower():
    """The lower floor: concrete slabs. 32 x 2 units, its top edge is the surface the operator stands on."""
    W, H = 32 * PPU, 2 * PPU
    c = Canvas(W, H)
    facets(c, 0, 0, W, H, CONCRETE, cell=56, amp=.07, seed=21, light=(0, -.9))
    for x in range(0, W + 1, 4 * PPU):
        c.rect(x - 2, 12, x + 2, H, (0, 0, 0, 80))
        c.rect(x + 2, 12, x + 4, H, (255, 255, 255, 12))
    # The lip: a lighter slab edge along the top, where the feet are.
    c.rect(0, 0, W, 10, mul(CONCRETE, 1.22))
    c.rect(0, 0, W, 3, (255, 255, 255, 70))
    c.rect(0, 10, W, 14, (0, 0, 0, 85))
    # Gravel and cracks.
    rnd = random.Random(2)
    for _ in range(120):
        x, y = rnd.randint(0, W), rnd.randint(18, H - 4)
        c.poly([(x, y), (x + rnd.randint(2, 7), y + rnd.randint(-1, 2)), (x + rnd.randint(0, 4), y + rnd.randint(2, 5))], (0, 0, 0, rnd.randint(30, 70)))
    c.save(os.path.join(OUT, "floor_lower.png"))


def floor_upper():
    """The upper floor, a riveted steel deck: 18 units wide and 0.4 deep (the deck plus a little of the beam)."""
    W, H = 18 * PPU, 32
    c = Canvas(W, H)
    facets(c, 0, 0, W, H, mul(STEEL, 1.0), cell=32, amp=.06, seed=31, light=(0, -.8))
    c.rect(0, 0, W, 9, mul(STEEL, 1.25))
    c.rect(0, 0, W, 2, (255, 255, 255, 80))
    c.rect(0, 9, W, 12, (0, 0, 0, 80))
    for x in range(40, W, 96):
        rivet(c, x, 5, 1.8, STEEL)
    # The beam underneath: a flange and web.
    c.rect(0, 22, W, H, mul(STEEL, .72))
    c.rect(0, 20, W, 23, (0, 0, 0, 70))
    c.save(os.path.join(OUT, "floor_upper.png"))


def shaft_back():
    """The freight lift's shaft: dark steel plate, 3.7 x 14 units."""
    W, H = int(3.7 * PPU), 14 * PPU
    c = Canvas(W, H)
    facets(c, 0, 0, W, H, hexc("#171c26"), cell=44, amp=.09, seed=41, light=(.2, -.5))
    for y in range(0, H, 128):
        c.rect(0, y, W, y + 3, (0, 0, 0, 90))
        c.rect(0, y + 3, W, y + 5, (255, 255, 255, 12))
    for x in (6, W - 6):
        for y in range(20, H, 128):
            rivet(c, x, y, 2.0, hexc("#303a4a"))
    c.save(os.path.join(OUT, "shaft_back.png"))


def window():
    """The window onto the blizzard, 6 x 3.5 units: a steel frame, glass, and a storm of snow beyond."""
    W, H = 6 * PPU, int(3.5 * PPU)
    c = Canvas(W, H)
    fr = 14
    # The storm: a grey-blue sky, layered faceted drifts, streaks of snow.
    vgrad(c, fr, fr, W - fr, H - fr, hexc("#6f7f95"), hexc("#b8c8da"))
    rnd = random.Random(7)
    for layer, (col, base_y, amp) in enumerate([("#c7d6e6", .68, 26), ("#dbe7f3", .78, 22), ("#f1f6fb", .9, 16)]):
        pts = [(fr, H - fr)]
        x = fr
        while x < W - fr:
            pts.append((x, H * base_y + rnd.uniform(-amp, amp)))
            x += rnd.uniform(24, 56)
        pts.append((W - fr, H * base_y))
        pts.append((W - fr, H - fr))
        c.poly(pts, hexc(col))
        for i in range(len(pts) - 3):
            tri = [pts[i + 1], pts[i + 2], (pts[i + 1][0], H - fr)]
            c.poly(tri, (255, 255, 255, 18 + 10 * layer))
    for _ in range(120):
        x, y = rnd.uniform(fr, W - fr), rnd.uniform(fr, H - fr)
        l = rnd.uniform(10, 34)
        c.poly([(x, y), (x + 2.2, y), (x - l * .5 + 2.2, y + l), (x - l * .5, y + l)], (255, 255, 255, rnd.randint(70, 190)))
    # Glass: a pale diagonal sheen, in low-poly facets.
    c.poly([(fr, fr), (fr + 150, fr), (fr + 70, H - fr), (fr, H - fr)], (255, 255, 255, 22))
    c.poly([(fr + 190, fr), (fr + 240, fr), (fr + 150, H - fr), (fr + 100, H - fr)], (255, 255, 255, 14))
    # Frame and a cross bar.
    for (x0, y0, x1, y1) in [(0, 0, W, fr), (0, H - fr, W, H), (0, 0, fr, H), (W - fr, 0, W, H)]:
        facets(c, x0, y0, x1, y1, STEEL, cell=14, amp=.1, seed=51, light=(0, -1))
    mid = W // 2
    facets(c, mid - 5, 0, mid + 5, H, STEEL, cell=14, amp=.1, seed=52, light=(0, -1))
    c.rect(0, H - fr, W, H - fr + 3, (0, 0, 0, 90))
    c.rect(0, 0, W, 3, (255, 255, 255, 60))
    # Frost creeping in at the corners.
    for cx, cy in [(fr, fr), (W - fr, fr)]:
        for i in range(9):
            a = rnd.uniform(0, 1.57) if cx < W / 2 else rnd.uniform(1.57, 3.14)
            r = rnd.uniform(20, 60)
            c.poly([(cx, cy), (cx + math.cos(a) * r, cy + math.sin(a) * r), (cx + math.cos(a + .2) * r * .7, cy + math.sin(a + .2) * r * .7)], (240, 250, 255, 70))
    c.save(os.path.join(OUT, "window.png"))


if __name__ == "__main__":
    wall()
    floor_lower()
    floor_upper()
    shaft_back()
    window()
    print("environment written to", os.path.abspath(OUT))
