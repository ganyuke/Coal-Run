"""The furnace, hopper, chute, stairs, control room, staff lift and the props round the room. Run from the repo root:
    Tools/ArtGen/.venv/bin/python Tools/ArtGen/make_structures.py
"""
import os

from PIL import ImageFont, ImageChops

from lowpoly import *

OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "Art", "Generated")
BOLD = "/usr/share/fonts/truetype/DejaVuSans-Bold.ttf"
STEEL = hexc("#5c6b80")
DARK = hexc("#232a36")
BRICK = hexc("#8f3f2d")
RUST = hexc("#b45a2e")
YELLOW = hexc("#e8b923")


def save(c, name):
    c.save(os.path.join(OUT, name + ".png"))
    return c


def text(c, s, x, y, size, fill, anchor="mm"):
    f = ImageFont.truetype(BOLD, int(size * SS))
    c.d.text((x * SS, y * SS), s, font=f, fill=fill, anchor=anchor)


def masked(c, poly):
    """Keeps only what's inside the polygon (final-pixel coordinates)."""
    m = Image.new("L", c.img.size, 0)
    ImageDraw.Draw(m).polygon([(x * SS, y * SS) for x, y in poly], fill=255)
    c.img.putalpha(ImageChops.multiply(c.img.split()[3], m))
    return c


def arch(cx, top, bottom, half_w, segs=14):
    pts = [(cx - half_w, bottom), (cx - half_w, top + half_w)]
    for i in range(1, segs):
        a = math.pi * i / segs
        pts.append((cx - math.cos(a) * half_w, top + half_w - math.sin(a) * half_w))
    pts += [(cx + half_w, top + half_w), (cx + half_w, bottom)]
    return pts


def furnace():
    W, H = int(3.2 * PPU), int(3.6 * PPU)
    c = Canvas(W, H)
    # Bricks.
    rnd = random.Random(4)
    bh, bw = 16, 34
    for row in range(H // bh + 1):
        off = 0 if row % 2 == 0 else bw // 2
        for x in range(-off, W, bw):
            base = mul(BRICK, rnd.uniform(.86, 1.12))
            x0, y0, x1, y1 = x + 1, row * bh + 1, x + bw - 1, row * bh + bh - 1
            c.poly([(x0, y0), (x1, y0), (x0, y1)], mul(base, 1.08))
            c.poly([(x1, y0), (x1, y1), (x0, y1)], mul(base, .92))
    c.rect(0, 0, W, H, (0, 0, 0, 0))
    # Mortar shadow near the base and soot over the mouth.
    for s in range(30):
        c.rect(0, H - 36 + s * 1.2, W, H - 36 + (s + 1) * 1.2, (0, 0, 0, int(s * 2.4)))
    # Plinth and cap.
    facets(c, 0, H - 16, W, H, mul(STEEL, .8), cell=24, amp=.08, seed=2, light=(0, -1))
    facets(c, -2, 0, W + 2, 14, mul(STEEL, 1.1), cell=24, amp=.08, seed=3, light=(0, -1))
    c.rect(0, 14, W, 17, (0, 0, 0, 100))
    c.rect(0, 0, W, 2, (255, 255, 255, 80))
    # Straps down both sides.
    for x0 in (0, W - 12):
        facets(c, x0, 14, x0 + 12, H - 16, mul(STEEL, .95), cell=20, amp=.08, seed=7, light=(1, 0))
        for y in range(34, H - 20, 44):
            rivet(c, x0 + 6, y, 2.0, STEEL)
    # The iron arch round the mouth (opening is x 38..166, y 77..192).
    outer = arch(W / 2, 62, 194, 72)
    inner = arch(W / 2, 77, 194, 64)
    c.poly(outer, mul(DARK, 1.3))
    ring_pts = [outer[i] for i in range(len(outer))]
    for i in range(len(outer) - 1):
        a, b, ia, ib = outer[i], outer[i + 1], inner[i], inner[i + 1]
        c.poly([a, b, ib, ia], mul(hexc("#39424f"), .85 + .3 * (i % 3) / 2))
    c.poly(inner, (0, 0, 0, 255))
    # A nameplate and a vent.
    c.rect(W / 2 - 28, 30, W / 2 + 28, 50, hexc("#2d3440"))
    text(c, "No. 1", W / 2, 40, 13, hexc("#e7c860"))
    for i in range(4):
        c.rect(W / 2 - 22 + i * 12, 22, W / 2 - 14 + i * 12, 26, hexc("#14171d"))
    return save(c, "furnace_body")


def furnace_mouth():
    """The inside of the firebox, in greys: the code tints it from dark to glowing with the fire."""
    W, H = 128, 115
    c = Canvas(W, H)
    sphere_facets(c, W / 2, H * .72, W * .66, hexc("#e8e8e8"), sides=11, seed=3, rot=.4)
    facets(c, 0, 0, W, 30, hexc("#9a9a9a"), cell=18, amp=.15, seed=8, light=(0, 1))
    masked(c, arch(W / 2, 0, H, W / 2))
    return save(c, "furnace_mouth")


def coal_bed():
    W, H = 122, 24
    c = Canvas(W, H)
    rnd = random.Random(9)
    x = 0
    while x < W:
        w = rnd.randint(14, 26)
        h = rnd.randint(8, 20)
        pts = [(x, H), (x + 2, H - h * .6), (x + w * .35, H - h), (x + w * .75, H - h * .8), (x + w, H - h * .3), (x + w, H)]
        fan(c, pts, hexc("#d8d8d8"), seed=int(x), amp=.2)
        x += w * .7
    return save(c, "coal_bed")


def hopper_back():
    W, H = int(2.2 * PPU), int(2.9 * PPU)
    c = Canvas(W, H)
    vgrad(c, 0, 0, W, H, hexc("#1b2029"), hexc("#2b323f"))
    facets(c, 0, 0, W, H, hexc("#222833"), cell=34, amp=.06, seed=5, light=(0, -.3))
    # Gauge ticks every quarter, up the left side.
    for i in range(1, 4):
        y = H - i * H / 4
        c.rect(6, y - 1, 30, y + 1, hexc("#7e8aa0"))
    # Frame and gussets.
    for (x0, y0, x1, y1) in [(0, 0, W, 10), (0, H - 10, W, H), (0, 0, 10, H), (W - 10, 0, W, H)]:
        facets(c, x0, y0, x1, y1, STEEL, cell=14, amp=.1, seed=6, light=(0, -1))
    for sx, sy in [(10, 10), (W - 10, 10), (10, H - 10), (W - 10, H - 10)]:
        c.poly([(sx, sy), (sx + (24 if sx < W / 2 else -24), sy), (sx, sy + (24 if sy < H / 2 else -24))], mul(STEEL, .9))
    for x in (5, W - 5):
        for y in range(18, H, 32):
            rivet(c, x, y, 1.8, STEEL)
    text(c, "HOPPER", W / 2, 24, 13, hexc("#8d99ad"))
    bevel(c, 0, 0, W, H, w=3, hi=.15, lo=.3)
    return save(c, "hopper_back")


def coal_fill():
    W, H = 64, 64
    c = Canvas(W, H)
    facets(c, 0, 0, W, H, hexc("#12141a"), cell=14, amp=.18, seed=12, light=(0, -.3))
    rnd = random.Random(2)
    for _ in range(18):
        x, y = rnd.uniform(0, W), rnd.uniform(2, H)
        s = rnd.uniform(4, 9)
        c.poly([(x, y), (x + s, y + s * .3), (x + s * .5, y + s * .9)], (90, 100, 125, rnd.randint(40, 100)))
    c.rect(0, 0, W, 4, (70, 80, 100, 110))
    return save(c, "coal_fill")


def grate():
    W, H = 128, 12
    c = Canvas(W, H)
    c.rect(0, 0, W, H, hexc("#1a1e26"))
    for x in range(2, W, 8):
        c.rect(x, 0, x + 4, H, hexc("#6a7588"))
        c.rect(x, 0, x + 4, 2, (255, 255, 255, 80))
    return save(c, "grate")


def chute_pipe():
    W, H = int(1.0 * PPU), int(7.2 * PPU)
    c = Canvas(W, H)
    for i in range(8):
        x0, x1 = i * W / 8, (i + 1) * W / 8
        k = .7 + .5 * (1 - abs(i - 2.5) / 5)
        facets(c, x0, 0, x1, H, mul(hexc("#5f6e84"), k), cell=60, amp=.04, seed=i, light=(0, -.2))
    for y in range(0, H, 115):
        facets(c, -2, y, W + 2, y + 14, mul(STEEL, 1.1), cell=14, amp=.1, seed=y, light=(0, -1))
        c.rect(-2, y + 14, W + 2, y + 18, (0, 0, 0, 90))
        for x in range(8, W, 14):
            rivet(c, x, y + 7, 1.7, STEEL)
    rnd = random.Random(1)
    for _ in range(40):
        x, y = rnd.randint(4, W - 8), rnd.randint(0, H)
        c.rect(x, y, x + rnd.randint(2, 5), y + rnd.randint(20, 90), (0, 0, 0, rnd.randint(20, 50)))
    return save(c, "chute_pipe")


def hopper_chute():
    """The funnel under the hopper and the pipe that carries coal down into the furnace: 2.0 x 0.9 units."""
    W, H = int(2.0 * PPU), int(.9 * PPU)
    c = Canvas(W, H)
    # Funnel: wide at the hopper's floor, narrowing to the pipe.
    neck0, neck1 = W / 2 - 30, W / 2 + 30
    funnel = H * .45
    c.poly([(0, 0), (W, 0), (neck1, funnel), (neck0, funnel)], mul(STEEL, .8))
    c.poly([(0, 0), (W / 2, 0), (W / 2, funnel), (neck0, funnel)], mul(STEEL, .95))
    c.poly([(W / 2, 0), (W, 0), (neck1, funnel), (W / 2, funnel)], mul(STEEL, .62))
    # The pipe: faceted steel with a dark channel the coal falls through.
    facets(c, neck0, funnel, neck1, H, mul(STEEL, .9), cell=16, amp=.1, seed=21, light=(.5, 0))
    c.rect(neck0 + 12, funnel, neck1 - 12, H, hexc("#0f1217"))
    # Flanges where the pipe meets the funnel and the furnace.
    for y in (funnel - 2, H - 9):
        facets(c, neck0 - 8, y, neck1 + 8, y + 9, mul(STEEL, 1.15), cell=10, amp=.1, seed=int(y), light=(0, -1))
        c.rect(neck0 - 8, y + 9, neck1 + 8, y + 12, (0, 0, 0, 100))
        for x in (neck0 - 3, neck1 + 3):
            rivet(c, x, y + 4.5, 1.6, STEEL)
    c.rect(0, 0, W, 3, (0, 0, 0, 90))
    return save(c, "hopper_chute")


def hopper_grate():
    """The grate the cart tips onto, over the hopper's mouth: 2.0 x 0.32 units, a hazard-striped frame round dark gaps."""
    W, H = int(2.0 * PPU), 20
    c = Canvas(W, H)
    c.rect(0, 0, W, H, hexc("#0b0d11"))
    # Bars across, each a lit top face and a shaded front.
    for x in range(14, W - 14, 11):
        c.rect(x, 2, x + 6, H - 3, hexc("#7e8aa0"))
        c.rect(x, 2, x + 6, 5, (255, 255, 255, 110))
        c.rect(x + 4, 5, x + 6, H - 3, (0, 0, 0, 70))
    # Hazard stripes on the end caps and a bright lip on top.
    for x0, x1 in ((0, 14), (W - 14, W)):
        c.rect(x0, 0, x1, H, YELLOW)
        for y in range(-4, H, 8):
            c.poly([(x0, y + 4), (x1, y), (x1, y + 4), (x0, y + 8)], hexc("#1a1c20"))
    c.rect(0, 0, W, 3, mul(STEEL, 1.35))
    c.rect(0, H - 3, W, H, (0, 0, 0, 120))
    return save(c, "hopper_grate")


def bunker():
    W, H = int(3.0 * PPU), int(4.0 * PPU)
    c = Canvas(W, H)
    facets(c, 0, 0, W, H, hexc("#2b3340"), cell=44, amp=.07, seed=14, light=(0, -.4))
    for x in range(0, W, 64):
        c.rect(x - 1, 0, x + 2, H, (0, 0, 0, 90))
        for y in range(16, H, 40):
            rivet(c, x + 8, y, 1.8, STEEL)
    # A hazard band and the stencil.
    top = int(H * .55)
    c.rect(0, top, W, top + 30, hexc("#1d222c"))
    for x in range(-30, W, 34):
        c.poly([(x, top + 30), (x + 16, top + 30), (x + 30, top), (x + 14, top)], YELLOW)
    text(c, "COAL", W / 2, top + 80, 40, hexc("#c9d2e0"))
    text(c, "KEEP CLEAR", W / 2, top + 112, 13, hexc("#7f8ba0"))
    bevel(c, 0, 0, W, H, w=4, hi=.18, lo=.35)
    return save(c, "bunker")


def weigh_plate():
    W, H = int(2.8 * PPU), 12
    c = Canvas(W, H)
    c.rect(0, 0, W, H, hexc("#2b2d33"))
    for x in range(-12, W, 24):
        c.poly([(x, H), (x + 12, H), (x + 24, 0), (x + 12, 0)], YELLOW)
    c.rect(0, 0, W, 2, (255, 255, 255, 80))
    return save(c, "weigh_plate")


def stair_parts():
    t = Canvas(64, 64)
    facets(t, 0, 0, 64, 64, hexc("#5a6577"), cell=16, amp=.07, seed=3, light=(0, -1))
    for x in range(6, 64, 8):
        t.rect(x, 4, x + 3, 60, (0, 0, 0, 70))
    t.rect(0, 0, 64, 10, (255, 255, 255, 40))
    save(t, "tread")
    b = Canvas(64, 64)
    facets(b, 0, 0, 64, 64, hexc("#363e4b"), cell=20, amp=.08, seed=4, light=(0, -1))
    b.rect(0, 0, 64, 6, (255, 255, 255, 36))
    save(b, "beam_dark")
    r = Canvas(64, 64)
    facets(r, 0, 0, 64, 64, hexc("#9aa5b6"), cell=20, amp=.06, seed=5, light=(0, -1))
    r.rect(0, 0, 64, 20, (255, 255, 255, 50))
    save(r, "rail_light")


def control_room():
    W, H = int(4.3 * PPU), int(2.7 * PPU)
    c = Canvas(W, H)
    facets(c, 0, 0, W, H, hexc("#2c3a40"), cell=40, amp=.07, seed=21, light=(0, -.4))
    # A map board, a clock-less wall, a desk.
    c.rect(24, 26, 98, 80, hexc("#1a1f26"))
    c.rect(28, 30, 94, 76, hexc("#3a4a42"))
    for i in range(5):
        c.line([(32, 36 + i * 8), (90, 40 + i * 7)], (200, 210, 190, 60), 1)
    for x in range(0, W, 96):
        c.rect(x, 0, x + 2, H, (0, 0, 0, 70))
    c.rect(0, H - 40, W, H, hexc("#23292f"))
    c.rect(0, H - 40, W, H - 36, hexc("#46525a"))
    return save(c, "control_back")


def glass():
    W, H = int(3.6 * PPU), int(2.7 * PPU)
    c = Canvas(W, H)
    c.rect(0, 0, W, H, (150, 195, 225, 70))
    c.poly([(0, 0), (90, 0), (30, H), (0, H)], (255, 255, 255, 40))
    c.poly([(120, 0), (150, 0), (80, H), (50, H)], (255, 255, 255, 26))
    c.poly([(W - 120, 0), (W - 70, 0), (W - 150, H), (W - 200, H)], (255, 255, 255, 22))
    c.rect(0, 0, W, 3, (255, 255, 255, 90))
    c.rect(0, H - 3, W, H, (0, 0, 0, 70))
    return save(c, "glass")


def slab(name, w, h, seed):
    W, H = int(w * PPU), int(h * PPU)
    c = Canvas(W, H)
    facets(c, 0, 0, W, H, hexc("#6a707a"), cell=24, amp=.08, seed=seed, light=(0, -.9))
    c.rect(0, 0, W, 3, (255, 255, 255, 70))
    c.rect(0, H - 3, W, H, (0, 0, 0, 100))
    for x in range(30, W, 64):
        c.rect(x, 3, x + 2, H - 3, (0, 0, 0, 50))
    return save(c, name)


def staff_lift():
    W, H = int(2.5 * PPU), int(4.3 * PPU)
    c = Canvas(W, H)
    facets(c, 0, 0, W, H, hexc("#20252d"), cell=34, amp=.08, seed=33, light=(0, -.4))
    # The ceiling lamp strip and a handrail.
    c.rect(26, 8, W - 26, 16, hexc("#e8e0c0"))
    c.rect(26, 8, W - 26, 10, (255, 255, 255, 110))
    for x in (18, W - 18):
        c.rect(x - 2, 20, x + 2, H, hexc("#14171d"))
    c.rect(10, 140, W - 10, 146, hexc("#7e8aa0"))
    bevel(c, 0, 0, W, H, w=4, hi=.14, lo=.3)
    save(c, "staff_lift_car")
    # The door: brushed steel, a seam down the middle and narrow windows. A 1 x 1 unit sprite: the staff lift
    # scales it to slide it open, so it has to be stretched to size.
    d = Canvas(64, 64)
    facets(d, 0, 0, 64, 64, hexc("#68727f"), cell=14, amp=.05, seed=36, light=(1, -.2))
    for x in range(4, 64, 7):
        d.rect(x, 0, x + 1, 64, (255, 255, 255, 14))
    d.rect(30, 0, 34, 64, hexc("#1a1e24"))
    for x0 in (12, 38):
        d.rect(x0, 12, x0 + 14, 34, hexc("#1d252e"))
        d.rect(x0 + 1, 13, x0 + 13, 33, hexc("#324352"))
    d.rect(0, 58, 64, 64, hexc("#383f4a"))
    for x in range(-4, 64, 10):
        d.poly([(x, 64), (x + 5, 64), (x + 10, 58), (x + 5, 58)], YELLOW)
    bevel(d, 0, 0, 64, 64, w=2, hi=.16, lo=.3)
    return save(d, "staff_lift_door")


def phone_and_lamp():
    P = 128
    W, H = int(.30 * P), int(.5 * P)
    c = Canvas(W, H)
    fan(c, [(2, 6), (W - 2, 6), (W - 4, H - 4), (4, H - 4)], hexc("#c8332b"), seed=3)
    fan(c, [(5, 10), (W - 5, 10), (W - 5, 20), (5, 20)], hexc("#8a1d1a"), seed=4)
    c.ellipse(W / 2 - 9, 28, W / 2 + 9, 46, hexc("#e8e2d0"))
    for k in range(8):
        a = k * math.pi / 4
        c.ellipse(W / 2 + math.cos(a) * 6 - 1.2, 37 + math.sin(a) * 6 - 1.2, W / 2 + math.cos(a) * 6 + 1.2, 37 + math.sin(a) * 6 + 1.2, hexc("#8a1d1a"))
    save(c, "phone_red@128")
    L = Canvas(int(.5 * P), int(.22 * P))
    L.rect(0, 14, L.w, L.h, hexc("#2b3038"))
    fan(L, arch(L.w / 2, 0, 16, L.w / 2 - 2)[:], hexc("#dedede"), seed=2)
    save(L, "lamp_lens@128")


def counterweight_parts():
    W, H = int(.5 * PPU), int(1.2 * PPU)
    c = Canvas(W, H)
    facets(c, 0, 0, W, H, hexc("#3a4250"), cell=14, amp=.1, seed=9, light=(0, -1))
    for y in range(2, H, 16):
        c.rect(0, y, W, y + 1, (0, 0, 0, 100))
    c.rect(W / 2 - 4, 0, W / 2 + 4, 8, hexc("#8a93a3"))
    save(c, "counterweight")
    p = Canvas(96, 96)
    ring(p, 48, 48, 46, 38, hexc("#7e8aa0"), sides=16, seed=2)
    for k in range(4):
        a = k * math.pi / 4
        p.poly([(48 + math.cos(a) * 36, 48 + math.sin(a) * 36), (48 - math.cos(a) * 36, 48 - math.sin(a) * 36),
                (48 - math.cos(a) * 36 + 3, 48 - math.sin(a) * 36 + 3), (48 + math.cos(a) * 36 + 3, 48 + math.sin(a) * 36 + 3)], hexc("#566176"))
    sphere_facets(p, 48, 48, 8, hexc("#c6a24a"), sides=8)
    save(p, "pulley")
    r = Canvas(64, 64)
    r.rect(0, 0, 64, 64, hexc("#4a4338"))
    for y in range(-8, 64, 8):
        r.poly([(0, y), (64, y + 4), (64, y + 8), (0, y + 4)], hexc("#7d7260"))
    save(r, "rope")


def meter_parts():
    P = 128
    H = Canvas(int(1.0 * P), int(1.0 * P))
    facets(H, 0, 0, H.w, H.h, hexc("#3b4351"), cell=24, amp=.08, seed=4, light=(0, -1))
    for sx, sy in [(8, 8), (H.w - 8, 8), (8, H.h - 8), (H.w - 8, H.h - 8)]:
        rivet(H, sx, sy, 3, STEEL)
    bevel(H, 0, 0, H.w, H.h, w=4, hi=.2, lo=.35)
    save(H, "meter_housing@128")
    D = Canvas(int(.8 * P), int(.8 * P))
    s = D.w / 2
    sphere_facets(D, s, s, s - 1, hexc("#e6e1cf"), sides=24, seed=1, light=(-.2, -.3))
    for k in range(24):
        a = k * math.pi / 12
        D.line([(s + math.cos(a) * (s - 3), s + math.sin(a) * (s - 3)), (s + math.cos(a) * (s - 10 if k % 3 == 0 else s - 6), s + math.sin(a) * (s - 10 if k % 3 == 0 else s - 6))], (30, 30, 30, 255), 1.5)
    save(D, "meter_disc@128")


def tile():
    t = Canvas(64, 64)
    facets(t, 0, 0, 64, 64, hexc("#ffffff"), cell=22, amp=.05, seed=3, light=(0, -1))
    bevel(t, 0, 0, 64, 64, w=6, hi=.35, lo=.3)
    save(t, "tile")


def props():
    P = 128
    # Barrel.
    W, H = int(.7 * P), int(1.0 * P)
    b = Canvas(W, H)
    for i in range(7):
        k = .7 + .5 * (1 - abs(i - 2) / 4)
        facets(b, i * W / 7, 0, (i + 1) * W / 7, H, mul(hexc("#a8532c"), k), cell=40, amp=.04, seed=i, light=(0, -.2))
    for y in (10, H / 2, H - 12):
        b.rect(0, y - 3, W, y + 3, hexc("#3a1f12"))
        b.rect(0, y - 3, W, y - 1, (255, 255, 255, 45))
    b.rect(0, 0, W, 5, mul(hexc("#a8532c"), 1.3))
    b.poly([(W / 2 - 14, H / 2 - 14), (W / 2 + 14, H / 2 - 14), (W / 2, H / 2 + 12)], YELLOW)
    b.poly([(W / 2 - 8, H / 2 - 10), (W / 2 + 8, H / 2 - 10), (W / 2, H / 2 + 5)], hexc("#2b2118"))
    save(b, "barrel@128")
    # Crate.
    cw, ch = int(1.0 * 64), int(.9 * 64)
    c = Canvas(cw, ch)
    facets(c, 0, 0, cw, ch, hexc("#7a5a3a"), cell=20, amp=.08, seed=4, light=(0, -.5))
    for y in (ch / 3, 2 * ch / 3):
        c.rect(0, y - 1, cw, y + 1, hexc("#3f2e1d"))
    c.rect(0, 0, 6, ch, hexc("#3f2e1d"))
    c.rect(cw - 6, 0, cw, ch, hexc("#3f2e1d"))
    c.poly([(6, 6), (cw - 6, ch - 6), (cw - 10, ch - 6), (6, 10)], hexc("#3f2e1d"))
    bevel(c, 0, 0, cw, ch, w=3, hi=.18, lo=.3)
    save(c, "crate")
    # Pipe pieces (stretched), flange, valve, gauge.
    p = Canvas(64, 16)
    for i in range(4):
        k = .75 + .45 * (1 - abs(i - 1.2) / 3)
        p.rect(0, i * 4, 64, i * 4 + 4, mul(hexc("#6a788c"), k))
    p.rect(0, 0, 64, 2, (255, 255, 255, 70))
    save(p, "pipe_h")
    pv = Canvas(16, 64)
    for i in range(4):
        k = .75 + .45 * (1 - abs(i - 1.2) / 3)
        pv.rect(i * 4, 0, i * 4 + 4, 64, mul(hexc("#6a788c"), k))
    pv.rect(0, 0, 2, 64, (255, 255, 255, 70))
    save(pv, "pipe_v")
    fl = Canvas(28, 28)
    facets(fl, 0, 0, 28, 28, hexc("#7a88a0"), cell=14, amp=.1, seed=2, light=(-1, -1))
    for sx, sy in [(4, 4), (24, 4), (4, 24), (24, 24)]:
        rivet(fl, sx, sy, 2, STEEL)
    save(fl, "flange")
    v = Canvas(int(.6 * P), int(.6 * P))
    s = v.w / 2
    ring(v, s, s, s - 1, s - 7, hexc("#c8332b"), sides=14, seed=3)
    for k in range(4):
        a = k * math.pi / 2
        v.poly([(s + math.cos(a) * 4 - math.sin(a) * 2, s + math.sin(a) * 4 + math.cos(a) * 2), (s + math.cos(a) * (s - 7) - math.sin(a) * 2, s + math.sin(a) * (s - 7) + math.cos(a) * 2),
                (s + math.cos(a) * (s - 7) + math.sin(a) * 2, s + math.sin(a) * (s - 7) - math.cos(a) * 2), (s + math.cos(a) * 4 + math.sin(a) * 2, s + math.sin(a) * 4 - math.cos(a) * 2)], hexc("#a52620"))
    sphere_facets(v, s, s, 6, hexc("#c6a24a"), sides=8)
    save(v, "valve@128")
    g = Canvas(int(.5 * P), int(.5 * P))
    s = g.w / 2
    sphere_facets(g, s, s, s - 1, hexc("#4b5565"), sides=16, seed=2)
    sphere_facets(g, s, s, s - 6, hexc("#e8e4d4"), sides=16, seed=3, light=(-.2, -.3))
    for k in range(11):
        a = math.pi * .75 + k * math.pi * 1.5 / 10
        g.line([(s + math.cos(a) * (s - 9), s + math.sin(a) * (s - 9)), (s + math.cos(a) * (s - 13), s + math.sin(a) * (s - 13))], (30, 30, 30, 255), 1.4)
    g.poly([(s, s), (s + math.cos(math.pi * 1.3) * (s - 11), s + math.sin(math.pi * 1.3) * (s - 11)), (s + 2, s + 2)], hexc("#c8332b"))
    sphere_facets(g, s, s, 3, hexc("#2b2118"), sides=6)
    save(g, "gauge@128")
    # Warning sign and a poster.
    sg = Canvas(int(.7 * P), int(.7 * P))
    s = sg.w / 2
    sg.poly([(s, 4), (sg.w - 4, sg.h - 8), (4, sg.h - 8)], hexc("#e8b923"))
    sg.poly([(s, 12), (sg.w - 12, sg.h - 13), (12, sg.h - 13)], hexc("#2b2118"))
    sg.poly([(s - 3, 26), (s + 3, 26), (s + 2, 50), (s - 2, 50)], hexc("#e8b923"))
    sg.ellipse(s - 3, 54, s + 3, 60, hexc("#e8b923"))
    save(sg, "sign_warning@128")
    po = Canvas(int(.9 * 64), int(1.2 * 64))
    po.rect(0, 0, po.w, po.h, hexc("#d8cfb4"))
    facets(po, 0, 0, po.w, po.h, hexc("#d8cfb4"), cell=14, amp=.03, seed=4, light=(0, 0))
    po.rect(4, 4, po.w - 4, 26, hexc("#b0302a"))
    text(po, "KEEP", po.w / 2, 15, 14, hexc("#f1e8cc"))
    text(po, "THE FIRE", po.w / 2, 38, 10, hexc("#3b2a1f"))
    text(po, "LIT", po.w / 2, 56, 20, hexc("#b0302a"))
    po.poly([(po.w / 2, 62), (po.w / 2 + 12, 76), (po.w / 2, 72), (po.w / 2 - 12, 76)], hexc("#e0902c"))
    bevel(po, 0, 0, po.w, po.h, w=2, hi=.1, lo=.25)
    save(po, "poster_fire")
    # Hanging cage lamp: an enamel shade over the bulb, its lit underside showing, and a wire guard round the bulb.
    lp = Canvas(int(.5 * P), int(.7 * P))
    cx = lp.w / 2
    wire = hexc("#1e1e22")
    lp.rect(cx - 1.5, 0, cx + 1.5, 12, wire)
    lp.rect(cx - 5, 10, cx + 5, 19, hexc("#2e3036"))
    lp.ellipse(cx - 24, 30, cx + 24, 40, hexc("#d9a24e"))
    sphere_facets(lp, cx, 41, 10, hexc("#ffe7a0"), sides=10, seed=2, light=(0, -.6))
    sphere_facets(lp, cx, 42, 5, hexc("#fffbea"), sides=8, seed=5)
    # The guard: wires from the rim down to a small ring under the bulb, with a hoop round its middle.
    for x in (-15, -5, 5, 15):
        lp.line([(cx + x, 35), (cx + x * 1.05, 46), (cx + x * .45, 56)], wire, 1.5)
    lp.line([(cx - 16, 45), (cx + 16, 45)], wire, 1.5)
    lp.line([(cx - 8, 56), (cx + 8, 56)], wire, 2)
    # The shade goes on last, so it covers the top of the bulb.
    fan(lp, [(cx - 26, 35), (cx - 8, 17), (cx + 8, 17), (cx + 26, 35)], hexc("#3e5440"), seed=3)
    lp.line([(cx - 26, 35), (cx + 26, 35)], hexc("#7a9474"), 2.5)
    save(lp, "lamp_cage@128")


if __name__ == "__main__":
    furnace(); furnace_mouth(); coal_bed(); hopper_back(); coal_fill(); grate(); hopper_chute(); hopper_grate(); chute_pipe(); bunker(); weigh_plate()
    stair_parts(); control_room(); glass(); slab("slab_concrete", 4.3, .4, 5); staff_lift(); phone_and_lamp()
    counterweight_parts(); meter_parts(); tile(); props()
    names = ["furnace_body", "furnace_mouth", "coal_bed", "hopper_back", "coal_fill", "grate", "hopper_chute", "hopper_grate", "chute_pipe", "bunker", "weigh_plate",
             "tread", "beam_dark", "rail_light", "control_back", "glass", "slab_concrete", "staff_lift_car", "staff_lift_door",
             "phone_red@128", "lamp_lens@128", "counterweight", "pulley", "rope", "meter_housing@128", "meter_disc@128", "tile",
             "barrel@128", "crate", "pipe_h", "pipe_v", "flange", "valve@128", "gauge@128", "sign_warning@128", "poster_fire", "lamp_cage@128"]
    ims = [Image.open(os.path.join(OUT, n + ".png")) for n in names]
    preview(ims, os.path.join(os.path.dirname(__file__), "preview_structures.png"), scale=1.0, cols=9)
    print("structures written")
