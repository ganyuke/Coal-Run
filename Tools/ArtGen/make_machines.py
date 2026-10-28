"""Characters, the cart, levers, wheels and the platform. Run from the repo root:
    Tools/ArtGen/.venv/bin/python Tools/ArtGen/make_machines.py
"""
import os

from lowpoly import *

OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "Art", "Generated")
os.makedirs(OUT, exist_ok=True)

STEEL = hexc("#5c6b80")
DARK = hexc("#262d39")
RUST = hexc("#b45a2e")
SKIN = hexc("#e8b88f")


def save(c, name):
    c.save(os.path.join(OUT, name + ".png"))
    return c


def worker_body(name, overalls, shirt, trousers, seed=1, arms=True):
    """A worker seen from the front, 0.4 x 1.0 units at 192 px a unit: boots, overalls, straps, arms and hands.
    Without arms it's the body the operator wears with the shovel out, its arms drawn separately (worker_arm)."""
    P = 192
    W, H = int(.40 * P), int(1.0 * P)
    c = Canvas(W, H)
    cx = W / 2
    # Boots and legs.
    for side in (-1, 1):
        x0 = cx + side * 3 - (22 if side < 0 else 0)
        fan(c, [(x0, H * .62), (x0 + 22, H * .62), (x0 + 22, H * .93), (x0, H * .93)], trousers, seed + side)
        fan(c, [(x0 - 2, H * .90), (x0 + 24, H * .90), (x0 + 26, H), (x0 - 4, H)], hexc("#1b1d22"), seed + 5 + side)
    # Torso: shirt with the overalls over it.
    torso = [(cx - 26, H * .22), (cx + 26, H * .22), (cx + 30, H * .64), (cx - 30, H * .64)]
    fan(c, torso, overalls, seed + 2)
    # Bib and straps.
    fan(c, [(cx - 15, H * .30), (cx + 15, H * .30), (cx + 17, H * .50), (cx - 17, H * .50)], mul(overalls, 1.08), seed + 3)
    for side in (-1, 1):
        c.poly([(cx + side * 8, H * .22), (cx + side * 17, H * .22), (cx + side * 21, H * .31), (cx + side * 12, H * .31)], mul(overalls, .72))
    # Pocket, belt and buckles.
    c.poly([(cx - 10, H * .38), (cx + 10, H * .38), (cx + 11, H * .44), (cx - 11, H * .44)], mul(overalls, .8))
    c.rect(cx - 30, H * .58, cx + 30, H * .62, hexc("#2b2118"))
    c.rect(cx - 4, H * .575, cx + 4, H * .625, hexc("#d6b24a"))
    # Arms: shirt sleeves hanging at the sides, with hands.
    for side in (-1, 1) if arms else ():
        x = cx + side * 33
        fan(c, [(x - 8, H * .23), (x + 8, H * .23), (x + 7, H * .53), (x - 7, H * .53)], shirt, seed + 8 + side)
        fan(c, [(x - 7, H * .52), (x + 7, H * .52), (x + 5, H * .61), (x - 5, H * .61)], SKIN, seed + 11 + side)
    # Shoulders: a short neck.
    c.poly([(cx - 9, H * .20), (cx + 9, H * .20), (cx + 7, H * .24), (cx - 7, H * .24)], mul(SKIN, .88))
    return save(c, name + "@192")


def worker_arm(name, shirt, seed=1):
    """One arm on its own, hanging straight down from the shoulder at the top: a sleeve and a hand, the same size
    as the ones on the body (0.083 x 0.4 units at 192 px a unit). The operator swings these to hold the shovel."""
    P = 192
    W, H = 18, int(.40 * P)
    c = Canvas(W, H)
    x = W / 2
    fan(c, [(x - 8, 0), (x + 8, 0), (x + 7, H * .78), (x - 7, H * .78)], shirt, seed)
    fan(c, [(x - 7, H * .76), (x + 7, H * .76), (x + 5, H), (x - 5, H)], SKIN, seed + 3)
    return save(c, name + "@192")


def worker_head(name, hat, hat_kind, seed=1, hair=None):
    """The head with a hard hat or a cap, 0.4 x 0.42 units at 192 px a unit."""
    P = 192
    W, H = int(.40 * P), int(.42 * P)
    c = Canvas(W, H)
    cx = W / 2
    # Head.
    face = [(cx - 19, H * .36), (cx + 19, H * .36), (cx + 22, H * .62), (cx + 12, H * .93), (cx - 12, H * .93), (cx - 22, H * .62)]
    fan(c, face, SKIN, seed, amp=.08)
    # Ears.
    for side in (-1, 1):
        c.poly([(cx + side * 21, H * .56), (cx + side * 28, H * .60), (cx + side * 22, H * .72)], mul(SKIN, .9))
    # Eyes, brows, a nose, a stubbly jaw.
    for side in (-1, 1):
        c.ellipse(cx + side * 9 - 3, H * .58 - 3, cx + side * 9 + 3, H * .58 + 3, hexc("#1d1a1a"))
        c.poly([(cx + side * 5, H * .50), (cx + side * 15, H * .48 + side * 1), (cx + side * 15, H * .52), (cx + side * 5, H * .53)], hexc("#3a2a20"))
    c.poly([(cx - 3, H * .62), (cx + 3, H * .62), (cx, H * .72)], mul(SKIN, .82))
    c.poly([(cx - 12, H * .93), (cx + 12, H * .93), (cx + 7, H * .78), (cx - 7, H * .78)], (60, 40, 30, 60))
    c.poly([(cx - 6, H * .83), (cx + 6, H * .83), (cx + 4, H * .86), (cx - 4, H * .86)], hexc("#7a3b30"))
    # Hat.
    if hat_kind == "hard":
        dome = [(cx - 25, H * .40), (cx - 22, H * .20), (cx - 10, H * .07), (cx + 10, H * .07), (cx + 22, H * .20), (cx + 25, H * .40)]
        fan(c, dome, hat, seed + 3)
        c.poly([(cx - 3, H * .07), (cx + 3, H * .07), (cx + 4, H * .38), (cx - 4, H * .38)], mul(hat, 1.12))
        c.poly([(cx - 32, H * .44), (cx + 32, H * .44), (cx + 26, H * .36), (cx - 26, H * .36)], mul(hat, .8))
    else:
        dome = [(cx - 24, H * .40), (cx - 21, H * .22), (cx - 8, H * .12), (cx + 12, H * .12), (cx + 23, H * .24), (cx + 25, H * .40)]
        fan(c, dome, hat, seed + 3)
        c.poly([(cx - 28, H * .46), (cx + 10, H * .46), (cx + 24, H * .39), (cx - 24, H * .39)], mul(hat, .72))
        if hair:
            for side in (-1, 1):
                c.poly([(cx + side * 23, H * .40), (cx + side * 26, H * .62), (cx + side * 20, H * .6)], hair)
    return save(c, name + "@192")


def cart_parts():
    """The ore cart: a riveted bin, spoked wheels and a skid, at 64 px a unit."""
    # Bin 1.9 x 0.7.
    W, H = int(1.9 * PPU), int(.7 * PPU)
    c = Canvas(W, H)
    facets(c, 0, 0, W, H, RUST, cell=30, amp=.09, seed=61, light=(0, -.7))
    # Rim along the top, ribs down the side.
    c.rect(0, 0, W, 8, mul(RUST, 1.28))
    c.rect(0, 0, W, 2, (255, 255, 255, 70))
    c.rect(0, 8, W, 11, (0, 0, 0, 80))
    for x in range(16, W, 28):
        c.rect(x, 11, x + 3, H, (0, 0, 0, 70))
        c.rect(x + 3, 11, x + 5, H, (255, 255, 255, 22))
        rivet(c, x + 1.5, 18, 1.8, RUST)
        rivet(c, x + 1.5, H - 9, 1.8, RUST)
    c.rect(0, H - 4, W, H, (0, 0, 0, 90))
    bevel(c, 0, 0, W, H, w=3, hi=.18, lo=.25)
    save(c, "cart_bin")
    # Wheel 0.6 across: tyre, rim, hub, spokes (low-poly), at 128 px a unit for crispness.
    P = 128
    S = int(.6 * P)
    w = Canvas(S, S)
    cxy = S / 2
    ring(w, cxy, cxy, S / 2 - 1, S / 2 - 8, hexc("#1d2026"), sides=16, seed=3)
    ring(w, cxy, cxy, S / 2 - 8, S / 2 - 13, hexc("#8b97a8"), sides=16, seed=4)
    for k in range(6):
        a = k * math.pi / 3
        w.poly([(cxy + math.cos(a) * 6 - math.sin(a) * 3, cxy + math.sin(a) * 6 + math.cos(a) * 3),
                (cxy + math.cos(a) * (S / 2 - 12) - math.sin(a) * 3, cxy + math.sin(a) * (S / 2 - 12) + math.cos(a) * 3),
                (cxy + math.cos(a) * (S / 2 - 12) + math.sin(a) * 3, cxy + math.sin(a) * (S / 2 - 12) - math.cos(a) * 3),
                (cxy + math.cos(a) * 6 + math.sin(a) * 3, cxy + math.sin(a) * 6 - math.cos(a) * 3)], hexc("#6f7c90"))
    sphere_facets(w, cxy, cxy, 8, hexc("#c6a24a"), sides=8)
    save(w, "wheel_spoked@128")
    # Skid: a flat steel runner.
    sk = Canvas(int(.3 * PPU), int(.6 * PPU))
    facets(sk, 0, 0, sk.w, sk.h, hexc("#4b5360"), cell=16, amp=.1, seed=71, light=(0, -.6))
    sk.poly([(0, sk.h), (sk.w, sk.h), (sk.w - 3, sk.h - 5), (3, sk.h - 5)], (0, 0, 0, 90))
    save(sk, "skid")


def lever_parts():
    """A floor lever: base plate, rod, knob, at 64 px a unit (rod is a 1 x 1 unit sprite stretched)."""
    # A 1 x 1 unit sprite: the lever stretches it to a thin rod.
    r = Canvas(64, 64)
    facets(r, 0, 0, 64, 64, hexc("#8c97a8"), cell=16, amp=.06, seed=5, light=(-1, 0))
    r.rect(0, 0, 14, 64, (255, 255, 255, 80))
    r.rect(50, 0, 64, 64, (0, 0, 0, 80))
    save(r, "lever_rod")
    k = Canvas(48, 48)
    sphere_facets(k, 24, 24, 21, hexc("#d23a28"), sides=9, seed=2)
    k.ellipse(14, 12, 22, 20, (255, 255, 255, 80))
    save(k, "lever_knob")
    b = Canvas(int(.6 * PPU), 10)
    facets(b, 0, 0, b.w, b.h, hexc("#3a4150"), cell=12, amp=.1, seed=9, light=(0, -1))
    b.rect(0, 0, b.w, 2, (255, 255, 255, 70))
    rivet(b, 5, 5, 1.6)
    rivet(b, b.w - 5, 5, 1.6)
    save(b, "lever_base")


def crank_wheel():
    """The lift's hand wheels: a rim, a hub and spokes drawn as separate bars, at 128 px a unit."""
    P = 128
    S = int(1.2 * P)
    w = Canvas(S, S)
    cxy = S / 2
    ring(w, cxy, cxy, S / 2 - 1, S / 2 - 12, hexc("#7a8698"), sides=18, seed=8)
    ring(w, cxy, cxy, S / 2 - 12, S / 2 - 16, hexc("#2a303b"), sides=18, seed=9)
    sphere_facets(w, cxy, cxy, 12, hexc("#c8a247"), sides=8, seed=1)
    w.ellipse(cxy - 4, cxy - 4, cxy + 4, cxy + 4, hexc("#2b2118"))
    # Handle knobs round the rim.
    for k in range(6):
        a = k * math.pi / 3 + math.pi / 6
        sphere_facets(w, cxy + math.cos(a) * (S / 2 - 2), cxy + math.sin(a) * (S / 2 - 2), 6, hexc("#a8532c"), sides=6, seed=k)
    save(w, "crank_wheel@128")
    s = Canvas(64, 64)
    facets(s, 0, 0, 64, 64, hexc("#8a93a3"), cell=16, amp=.08, seed=4, light=(0, -1))
    s.rect(0, 0, 64, 14, (255, 255, 255, 70))
    s.rect(0, 50, 64, 64, (0, 0, 0, 80))
    save(s, "spoke")


def platform_parts():
    """The freight lift: a deck with hazard edges, tall side rails, a rail on top."""
    W, H = int(3.5 * PPU), int(.3 * PPU)
    c = Canvas(W, H)
    facets(c, 0, 0, W, H, hexc("#566176"), cell=22, amp=.08, seed=81, light=(0, -.7))
    c.rect(0, 0, W, 4, mul(hexc("#566176"), 1.28))
    c.rect(0, 0, W, 1, (255, 255, 255, 80))
    # Hazard stripes along the front edge.
    for x in range(-20, W, 22):
        c.poly([(x, H - 6), (x + 11, H - 6), (x + 17, H), (x + 6, H)], hexc("#e8b923"))
    c.rect(0, H - 6, W, H - 5, (0, 0, 0, 90))
    for x in range(10, W, 40):
        rivet(c, x, 7, 1.4)
    bevel(c, 0, 0, W, H, w=2, hi=.12, lo=.3)
    save(c, "lift_platform")
    # Post and top rail: orange tube, 1 x 1 unit stretched.
    t = Canvas(64, 64)
    facets(t, 0, 0, 64, 64, RUST, cell=16, amp=.07, seed=3, light=(-1, 0))
    t.rect(0, 0, 16, 64, (255, 255, 255, 60))
    t.rect(48, 0, 64, 64, (0, 0, 0, 70))
    save(t, "tube_orange")
    # Guide shoes.
    g = Canvas(16, 26)
    facets(g, 0, 0, 16, 26, hexc("#3b4250"), cell=10, amp=.1, seed=7, light=(0, -1))
    save(g, "shoe")


if __name__ == "__main__":
    worker_body("op_body", hexc("#e2872e"), hexc("#c9c2b4"), hexc("#31405a"), seed=1)
    worker_body("op_body_bare", hexc("#e2872e"), hexc("#c9c2b4"), hexc("#31405a"), seed=1, arms=False)
    worker_arm("op_arm", hexc("#c9c2b4"), seed=9)
    worker_head("op_head", hexc("#e8b923"), "hard", seed=2)
    worker_body("helper_body", hexc("#a39c78"), hexc("#7a7f8a"), hexc("#444a40"), seed=3)
    worker_head("helper_head", hexc("#6a6f78"), "cap", seed=4, hair=hexc("#8a8a8a"))
    cart_parts()
    lever_parts()
    crank_wheel()
    platform_parts()
    names = ["op_body@192", "op_body_bare@192", "op_arm@192", "op_head@192", "helper_body@192", "helper_head@192", "cart_bin", "wheel_spoked@128", "skid", "lever_knob", "lever_base", "crank_wheel@128", "lift_platform", "tube_orange", "shoe", "spoke"]
    from PIL import Image as _I
    ims = [_I.open(os.path.join(OUT, n + ".png")) for n in names]
    preview(ims, os.path.join(os.path.dirname(__file__), "preview_machines.png"), scale=1.0, cols=7)
    print("machines written")
