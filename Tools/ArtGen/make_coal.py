"""Coal: pile columns and lumps. Run from the repo root:
    Tools/ArtGen/.venv/bin/python Tools/ArtGen/make_coal.py
"""
import os

from lowpoly import *

OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "Art", "Generated")
BASE = hexc("#2b3342")


def column():
    """A pile column, a 1 x 1 unit sprite the field stretches to the pile's height: big dark facets with cold highlights."""
    c = Canvas(64, 64)
    facets(c, 0, 0, 64, 64, BASE, cell=18, amp=.45, seed=11, light=(.4, -.9), jitter=.38)
    rnd = random.Random(3)
    for _ in range(9):
        x, y = rnd.uniform(2, 58), rnd.uniform(6, 60)
        s = rnd.uniform(5, 11)
        c.poly([(x, y), (x + s, y + s * .25), (x + s * .4, y + s * .9)], (110, 130, 170, rnd.randint(40, 90)))
    # A lighter lip along the top, where the pile catches the light.
    c.poly([(0, 0), (64, 0), (64, 6), (0, 9)], (95, 112, 145, 120))
    c.poly([(0, 9), (64, 6), (64, 12), (0, 14)], (95, 112, 145, 45))
    c.save(os.path.join(OUT, "coal_column.png"))


def lump(i):
    c = Canvas(64, 64)
    rnd = random.Random(40 + i)
    n = rnd.randint(7, 9)
    cx, cy = 32, 33
    pts = []
    for k in range(n):
        a = 2 * math.pi * k / n + rnd.uniform(-.25, .25)
        r = rnd.uniform(20, 28)
        pts.append((cx + math.cos(a) * r, cy + math.sin(a) * r))
    fan(c, pts, mul(BASE, 1.05), seed=i, amp=.3, light=(-.7, -.8), edge=(150, 172, 215, 230))
    # One bright facet like a freshly broken face, and a few specks.
    a, b = pts[0], pts[1]
    c.poly([(cx, cy), a, b], (130, 150, 190, 70))
    for _ in range(2):
        x, y = rnd.uniform(18, 46), rnd.uniform(18, 46)
        c.poly([(x, y), (x + 5, y + 1), (x + 2, y + 5)], (170, 190, 230, 70))
    c.save(os.path.join(OUT, f"coal_lump{i}.png"))


if __name__ == "__main__":
    column()
    for i in range(3):
        lump(i)
    names = ["coal_column", "coal_lump0", "coal_lump1", "coal_lump2"]
    ims = [Image.open(os.path.join(OUT, n + ".png")) for n in names]
    preview(ims, os.path.join(os.path.dirname(__file__), "preview_coal.png"), scale=2, cols=4)
    print("coal written")
