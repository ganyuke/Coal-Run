"""The instrument panel: a thermometer, a chart and a clock plate on one steel board. Run from the repo root:
    Tools/ArtGen/.venv/bin/python Tools/ArtGen/make_instruments.py

Layout, in local units from the panel's centre (y up): the panel is 5 x 3. The temperature axis is shared by the
thermometer and the chart: T = -40 at y = -1.05 and T = +25 at y = +1.05. The thermometer's tube is at x = -2.28,
the scale labels sit between x = -2.05 and -1.6, and the chart runs from x = -1.55 to 2.25.
"""
import os

from PIL import ImageFont

from lowpoly import *

OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "Art", "Generated")
BOLD = "/usr/share/fonts/truetype/DejaVuSans-Bold.ttf"
STEEL = hexc("#5c6b80")

T_MIN, T_MAX = -40, 25
Y_MIN, Y_MAX = -1.05, 1.05


def y_of(t):
    return Y_MIN + (t - T_MIN) / (T_MAX - T_MIN) * (Y_MAX - Y_MIN)


def px(x, y, W, H):
    """Local units (y up) to canvas pixels (y down)."""
    return (x + 2.5) * PPU, (1.5 - y) * PPU


def label(c, s, x, y, size, fill, anchor="rm"):
    f = ImageFont.truetype(BOLD, int(size * SS))
    c.d.text((x * SS, y * SS), s, font=f, fill=fill, anchor=anchor)


def panel():
    W, H = 5 * PPU, 3 * PPU
    c = Canvas(W, H)
    facets(c, 0, 0, W, H, hexc("#2b3340"), cell=40, amp=.07, seed=4, light=(0, -.7))
    # The chart's recess.
    x0, y0 = px(-1.55, 1.1, W, H)
    x1, y1 = px(2.25, -1.1, W, H)
    c.rect(x0 - 5, y0 - 5, x1 + 5, y1 + 5, hexc("#111419"))
    c.rect(x0, y0, x1, y1, hexc("#1c2420"))
    facets(c, x0, y0, x1, y1, hexc("#1f2a25"), cell=30, amp=.04, seed=9, light=(0, -.3))
    # Gridlines every 10 degrees, brighter every 20; zero is the freezing line, ice blue.
    for t in range(T_MIN, T_MAX + 1, 10):
        _, y = px(0, y_of(t), W, H)
        c.rect(x0, y - 1, x1, y + 1, (130, 170, 150, 80 if t % 20 == 0 else 34))
    _, yz = px(0, y_of(0), W, H)
    c.rect(x0, yz - 2, x1, yz + 2, (120, 200, 255, 210))
    # The frozen zone under zero, faintly frosted.
    c.rect(x0, yz + 2, x1, y1, (120, 180, 255, 22))
    # The time axis: a tick every minute of the ten.
    for m in range(0, 11):
        x = x0 + (x1 - x0) * m / 10
        c.rect(x - 1, y1 - 7, x + 1, y1, (140, 180, 160, 150))
    # Scale labels.
    for t in (-40, -20, 0, 20):
        _, y = px(0, y_of(t), W, H)
        lx, _ = px(-1.6, 0, W, H)
        label(c, f"{t}°", lx, y, 14 if t != 0 else 17, hexc("#9fd3ff") if t == 0 else hexc("#aab6c8"))
    # The tube's housing on the left: a groove the tube sits in.
    tx, _ = px(-2.28, 0, W, H)
    _, ty0 = px(0, 1.36, W, H)
    _, ty1 = px(0, -1.46, W, H)
    c.rect(tx - 12, ty0, tx + 12, ty1, hexc("#14181f"))
    # Captions along the bottom.
    cy = H - 13
    for x, col, text in [(-1.5, hexc("#ffb050"), "INSIDE"), (-0.2, hexc("#9fd3ff"), "OUTSIDE"), (1.3, hexc("#ff8a3a"), "FIRE HOLDS")]:
        sx, _ = px(x, 0, W, H)
        c.rect(sx - 14, cy - 3, sx - 6, cy + 3, col)
        label(c, text, sx - 2, cy, 11, hexc("#9aa6b8"), "lm")
    # The clock plate, top right.
    px0, py0 = px(.55, 1.46, W, H)
    px1, py1 = px(2.25, 1.14, W, H)
    c.rect(px0, py0, px1, py1, hexc("#0e1210"))
    c.rect(px0 + 2, py0 + 2, px1 - 2, py1 - 2, hexc("#161d19"))
    label(c, "SHIFT", px0 + 10, (py0 + py1) / 2, 11, hexc("#5f7a6c"), "lm")
    # Frame, rivets, bevel.
    for sx, sy in [(10, 10), (W - 10, 10), (10, H - 10), (W - 10, H - 10)]:
        rivet(c, sx, sy, 3.2, STEEL)
    bevel(c, 0, 0, W, H, w=5, hi=.2, lo=.4)
    c.save(os.path.join(OUT, "instrument_panel.png"))


def tube():
    """A glass tube, 0.28 wide and 2.6 tall, with a bulb at the bottom, at 128 px a unit."""
    P = 128
    W, H = int(.5 * P), int(2.9 * P)
    c = Canvas(W, H)
    cx = W / 2
    # The bulb.
    sphere_facets(c, cx, H - 40, 34, hexc("#bfd8e8"), sides=14, seed=2, light=(-.5, -.6))
    # The tube: a pale glass column with a lighter edge.
    c.rect(cx - 15, 10, cx + 15, H - 50, hexc("#a8c4d6"))
    c.rect(cx - 15, 10, cx - 9, H - 50, (255, 255, 255, 120))
    c.rect(cx + 9, 10, cx + 15, H - 50, (0, 0, 0, 40))
    c.ellipse(cx - 15, 4, cx + 15, 24, hexc("#bfd8e8"))
    # The glass is hollow: a darker channel for the mercury to show in.
    c.rect(cx - 8, 14, cx + 8, H - 50, hexc("#23303a"))
    sphere_facets(c, cx, H - 40, 26, hexc("#23303a"), sides=14, seed=3, light=(0, 0))
    # Tick marks down the side every 10 degrees would clash with the chart's: a few short ones.
    c.save(os.path.join(OUT, "thermo_tube@128.png"))


def parts():
    # Mercury: a 1 x 1 unit sprite, stretched to the column and tinted by temperature.
    m = Canvas(64, 64)
    m.rect(0, 0, 64, 64, hexc("#ffffff"))
    m.rect(0, 0, 22, 64, (255, 255, 255, 140))
    m.rect(44, 0, 64, 64, (0, 0, 0, 40))
    m.save(os.path.join(OUT, "mercury.png"))
    b = Canvas(64, 64)
    sphere_facets(b, 32, 32, 28, hexc("#ffffff"), sides=14, seed=2, light=(-.5, -.6))
    b.save(os.path.join(OUT, "mercury_bulb.png"))
    # A little triangle marker, pointing left, tinted in code.
    t = Canvas(32, 32)
    t.poly([(4, 16), (28, 4), (28, 28)], hexc("#ffffff"))
    t.poly([(8, 16), (24, 8), (24, 24)], (0, 0, 0, 40))
    t.save(os.path.join(OUT, "marker.png"))
    # A pip for the line ends.
    d = Canvas(32, 32)
    sphere_facets(d, 16, 16, 14, hexc("#ffffff"), sides=10, seed=1)
    d.save(os.path.join(OUT, "pip.png"))
    # A dash for dotted lines: 2 px of white, 2 of nothing.
    dash = Image.new("RGBA", (2, 1), (0, 0, 0, 0))
    dash.putpixel((0, 0), (255, 255, 255, 255))
    dash.save(os.path.join(OUT, "dash.png"))


if __name__ == "__main__":
    panel()
    tube()
    parts()
    names = ["instrument_panel", "thermo_tube@128", "mercury", "mercury_bulb", "marker", "pip"]
    ims = [Image.open(os.path.join(OUT, n + ".png")) for n in names]
    preview(ims, os.path.join(os.path.dirname(__file__), "preview_instruments.png"), cols=6)
    print("instruments written")
