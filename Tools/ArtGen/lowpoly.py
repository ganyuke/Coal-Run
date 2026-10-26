"""Tiny low-poly drawing toolkit for Coal Run's art.

Everything is flat-shaded triangles lit from the top left, drawn supersampled and shrunk so edges are smooth.
Sizes are in pixels at the final resolution; a world unit is 64 px (Unity pixels per unit).
"""
import math
import random

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

PPU = 64
SS = 2  # supersampling factor


def hexc(h, a=255):
    h = h.lstrip("#")
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), a)


def mul(c, k):
    """Brighten or darken an RGB(A) colour."""
    return tuple(max(0, min(255, int(round(v * k)))) for v in c[:3]) + ((c[3],) if len(c) > 3 else (255,))


def mix(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3)) + (255,)


class Canvas:
    """An RGBA canvas drawn at SS x resolution. Coordinates are final pixels (floats ok), y down."""

    def __init__(self, w, h, bg=(0, 0, 0, 0)):
        self.w, self.h = w, h
        self.img = Image.new("RGBA", (w * SS, h * SS), bg)
        self.d = ImageDraw.Draw(self.img, "RGBA")

    def _s(self, pts):
        return [(x * SS, y * SS) for x, y in pts]

    def _blend(self, bbox, draw, fill):
        """Draws with a semi-transparent fill by compositing a small layer, since ImageDraw would overwrite."""
        x0, y0, x1, y1 = bbox
        x0, y0 = max(0, int(math.floor(x0)) - 1), max(0, int(math.floor(y0)) - 1)
        x1, y1 = min(self.img.width, int(math.ceil(x1)) + 2), min(self.img.height, int(math.ceil(y1)) + 2)
        if x1 <= x0 or y1 <= y0:
            return
        layer = Image.new("RGBA", (x1 - x0, y1 - y0), (0, 0, 0, 0))
        ld = ImageDraw.Draw(layer)
        draw(ld, -x0, -y0)
        self.img.alpha_composite(layer, (x0, y0))

    def poly(self, pts, fill):
        sp = self._s(pts)
        if len(fill) == 4 and fill[3] < 255:
            xs, ys = [p[0] for p in sp], [p[1] for p in sp]
            self._blend((min(xs), min(ys), max(xs), max(ys)),
                        lambda ld, ox, oy: ld.polygon([(x + ox, y + oy) for x, y in sp], fill=fill), fill)
        else:
            self.d.polygon(sp, fill=fill)

    def line(self, pts, fill, width=1):
        sp = self._s(pts)
        w = max(1, int(round(width * SS)))
        if len(fill) == 4 and fill[3] < 255:
            xs, ys = [p[0] for p in sp], [p[1] for p in sp]
            self._blend((min(xs) - w, min(ys) - w, max(xs) + w, max(ys) + w),
                        lambda ld, ox, oy: ld.line([(x + ox, y + oy) for x, y in sp], fill=fill, width=w), fill)
        else:
            self.d.line(sp, fill=fill, width=w)

    def rect(self, x0, y0, x1, y1, fill):
        box = (x0 * SS, y0 * SS, x1 * SS - 1, y1 * SS - 1)
        if len(fill) == 4 and fill[3] < 255:
            self._blend(box, lambda ld, ox, oy: ld.rectangle([box[0] + ox, box[1] + oy, box[2] + ox, box[3] + oy], fill=fill), fill)
        else:
            self.d.rectangle(list(box), fill=fill)

    def ellipse(self, x0, y0, x1, y1, fill):
        box = (x0 * SS, y0 * SS, x1 * SS - 1, y1 * SS - 1)
        if len(fill) == 4 and fill[3] < 255:
            self._blend(box, lambda ld, ox, oy: ld.ellipse([box[0] + ox, box[1] + oy, box[2] + ox, box[3] + oy], fill=fill), fill)
        else:
            self.d.ellipse(list(box), fill=fill)

    def paste(self, other, x, y):
        self.img.alpha_composite(other.img, (int(round(x * SS)), int(round(y * SS))))

    def blur_glow(self, radius):
        self.img = self.img.filter(ImageFilter.GaussianBlur(radius * SS))
        self.d = ImageDraw.Draw(self.img, "RGBA")

    def done(self):
        return self.img.resize((self.w, self.h), Image.LANCZOS)

    def save(self, path):
        self.done().save(path)


def facets(c, x0, y0, x1, y1, base, cell=40, amp=0.10, seed=0, light=(0.35, -0.45), jitter=0.30):
    """Fill a rectangle with jittered triangles, each shaded a little differently: the low-poly look.

    `light` skews the shading across the rectangle (positive x brightens to the right, negative y to the top).
    """
    rnd = random.Random(seed)
    cols = max(1, int(round((x1 - x0) / cell)))
    rows = max(1, int(round((y1 - y0) / cell)))
    cw, ch = (x1 - x0) / cols, (y1 - y0) / rows
    pts = {}
    for j in range(rows + 1):
        for i in range(cols + 1):
            x = x0 + i * cw
            y = y0 + j * ch
            if 0 < i < cols:
                x += rnd.uniform(-jitter, jitter) * cw
            if 0 < j < rows:
                y += rnd.uniform(-jitter, jitter) * ch
            pts[(i, j)] = (x, y)
    for j in range(rows):
        for i in range(cols):
            a, b, cc, d = pts[(i, j)], pts[(i + 1, j)], pts[(i + 1, j + 1)], pts[(i, j + 1)]
            tris = [(a, b, cc), (a, cc, d)] if rnd.random() < .5 else [(a, b, d), (b, cc, d)]
            for t in tris:
                mx = (t[0][0] + t[1][0] + t[2][0]) / 3
                my = (t[0][1] + t[1][1] + t[2][1]) / 3
                u = (mx - x0) / max(1, (x1 - x0)) - .5
                v = (my - y0) / max(1, (y1 - y0)) - .5
                k = 1 + light[0] * u * amp * 2 + light[1] * v * amp * 2 + rnd.uniform(-amp, amp)
                c.poly(t, mul(base, k))


def bevel(c, x0, y0, x1, y1, w=3, hi=0.25, lo=0.30):
    """Light top and left edge, dark bottom and right edge, so a slab reads as raised."""
    base = (0, 0, 0, 255)
    c.poly([(x0, y0), (x1, y0), (x1 - w, y0 + w), (x0 + w, y0 + w)], (255, 255, 255, int(255 * hi)))
    c.poly([(x0, y0), (x0 + w, y0 + w), (x0 + w, y1 - w), (x0, y1)], (255, 255, 255, int(255 * hi * .6)))
    c.poly([(x0, y1), (x1, y1), (x1 - w, y1 - w), (x0 + w, y1 - w)], (0, 0, 0, int(255 * lo)))
    c.poly([(x1, y0), (x1, y1), (x1 - w, y1 - w), (x1 - w, y0 + w)], (0, 0, 0, int(255 * lo * .8)))


def rivet(c, x, y, r=2.2, base=(120, 132, 150, 255)):
    c.ellipse(x - r, y - r, x + r, y + r, mul(base, .55))
    c.ellipse(x - r * .8, y - r * .8, x + r * .6, y + r * .6, mul(base, 1.2))


def vgrad(c, x0, y0, x1, y1, top, bottom, steps=48):
    """A vertical gradient drawn as bands (kept blocky on purpose)."""
    for s in range(steps):
        t0, t1 = s / steps, (s + 1) / steps
        c.rect(x0, y0 + (y1 - y0) * t0, x1, y0 + (y1 - y0) * t1 + 1, mix(top, bottom, (t0 + t1) / 2))


def drop_shadow(c, x0, y0, x1, y1, alpha=90, blur=6):
    layer = Canvas(c.w, c.h)
    layer.rect(x0, y0, x1, y1, (0, 0, 0, alpha))
    layer.blur_glow(blur)
    c.img.alpha_composite(layer.img)
    c.d = ImageDraw.Draw(c.img, "RGBA")


def sphere_facets(c, cx, cy, r, base, sides=9, seed=0, rot=0.3, light=(-0.6, -0.7)):
    """A faceted disc: a fan of triangles around the centre, shaded by facing."""
    rnd = random.Random(seed)
    for i in range(sides):
        a0 = rot + 2 * math.pi * i / sides
        a1 = rot + 2 * math.pi * (i + 1) / sides
        am = (a0 + a1) / 2
        facing = math.cos(am) * light[0] + math.sin(am) * light[1]
        k = .72 + .42 * (facing * .5 + .5) + rnd.uniform(-.04, .04)
        c.poly([(cx, cy), (cx + math.cos(a0) * r, cy + math.sin(a0) * r), (cx + math.cos(a1) * r, cy + math.sin(a1) * r)], mul(base, k))


def fan(c, pts, base, seed=0, amp=.12, light=(-0.6, -0.7), edge=None):
    """A polygon filled as a fan of triangles from its centre, each shaded by which way it faces."""
    rnd = random.Random(seed)
    cx = sum(p[0] for p in pts) / len(pts)
    cy = sum(p[1] for p in pts) / len(pts)
    for i in range(len(pts)):
        a, b = pts[i], pts[(i + 1) % len(pts)]
        mx, my = (a[0] + b[0]) / 2 - cx, (a[1] + b[1]) / 2 - cy
        n = math.hypot(mx, my) or 1
        facing = (mx * light[0] + my * light[1]) / n
        k = .86 + .26 * (facing * .5 + .5) + rnd.uniform(-amp, amp) * .5
        c.poly([(cx, cy), a, b], mul(base, k))
    if edge:
        c.line(list(pts) + [pts[0]], edge, 1)


def ring(c, cx, cy, r_out, r_in, base, sides=14, seed=0, rot=0.0):
    """A faceted ring: a band of quads around a hole."""
    rnd = random.Random(seed)
    for i in range(sides):
        a0 = rot + 2 * math.pi * i / sides
        a1 = rot + 2 * math.pi * (i + 1) / sides
        am = (a0 + a1) / 2
        facing = math.cos(am) * -.6 + math.sin(am) * -.7
        k = .72 + .5 * (facing * .5 + .5) + rnd.uniform(-.05, .05)
        c.poly([(cx + math.cos(a0) * r_in, cy + math.sin(a0) * r_in), (cx + math.cos(a0) * r_out, cy + math.sin(a0) * r_out),
                (cx + math.cos(a1) * r_out, cy + math.sin(a1) * r_out), (cx + math.cos(a1) * r_in, cy + math.sin(a1) * r_in)], mul(base, k))


def preview(items, path, pad=12, bg=(40, 48, 62, 255), scale=1.0, cols=6):
    """Lays finished sprites out on a dark sheet so a batch can be judged in one look."""
    ims = [i.done() if isinstance(i, Canvas) else i for i in items]
    ims = [i.resize((max(1, int(i.width * scale)), max(1, int(i.height * scale))), Image.LANCZOS) for i in ims]
    rows = [ims[i:i + cols] for i in range(0, len(ims), cols)]
    W = max(sum(i.width + pad for i in r) + pad for r in rows)
    H = sum(max(i.height for i in r) + pad for r in rows) + pad
    sheet = Image.new("RGBA", (W, H), bg)
    y = pad
    for r in rows:
        x = pad
        for i in r:
            sheet.alpha_composite(i, (x, y))
            x += i.width + pad
        y += max(i.height for i in r) + pad
    sheet.save(path)
