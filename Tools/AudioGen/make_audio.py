"""Synthesises the game's loops and effects as 16-bit mono WAVs. Run from the repo root:
    Tools/ArtGen/.venv/bin/python Tools/AudioGen/make_audio.py
(It borrows the art tool's venv for numpy.) The physical hits are Kenney's CC0 Impact Sounds, copied separately.
"""
import os
import wave

import numpy as np

SR = 44100
OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "Audio", "Synth")
os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(7)


def t(seconds):
    return np.arange(int(SR * seconds)) / SR


def noise(seconds):
    return rng.standard_normal(int(SR * seconds))


def band(x, lo, hi):
    """Band-pass by zeroing everything outside lo..hi Hz in the spectrum (smooth edges)."""
    X = np.fft.rfft(x)
    f = np.fft.rfftfreq(len(x), 1 / SR)
    w = np.ones_like(f)
    w *= 1 / (1 + np.exp(-(f - lo) / max(lo * .15, 5)))
    w *= 1 / (1 + np.exp((f - hi) / max(hi * .15, 5)))
    return np.fft.irfft(X * w, len(x))


def env(n, attack, release, curve=2.0):
    e = np.ones(n)
    a, r = int(SR * attack), int(SR * release)
    if a:
        e[:a] = np.linspace(0, 1, a) ** curve
    if r:
        e[-r:] *= np.linspace(1, 0, r) ** curve
    return e


def norm(x, peak=.85):
    return x / (np.max(np.abs(x)) + 1e-9) * peak


def loopable(x, fade):
    """Cross-fades the end into the start so the loop has no seam."""
    n = int(SR * fade)
    out = x[:-n].copy()
    out[:n] = out[:n] * np.linspace(0, 1, n) + x[-n:] * np.linspace(1, 0, n)
    return out


def save(name, x):
    x = np.clip(x, -1, 1)
    with wave.open(os.path.join(OUT, name + ".wav"), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((x * 32767).astype(np.int16).tobytes())


def clicks(seconds, rate, tone, decay, jitter=0.0, strength=1.0):
    """A string of little impacts: each a short decaying burst around `tone` Hz."""
    out = np.zeros(int(SR * seconds))
    n_clicks = int(rate * seconds)
    for i in range(n_clicks):
        at = int((i / rate + rng.uniform(-jitter, jitter) / rate) * SR)
        if at < 0 or at >= len(out) - 2000:
            continue
        L = int(SR * decay)
        k = np.arange(L) / SR
        f = tone * rng.uniform(.8, 1.25)
        burst = (np.sin(2 * np.pi * f * k) + .5 * rng.standard_normal(L)) * np.exp(-k / (decay * .22)) * rng.uniform(.4, 1) * strength
        out[at:at + L] += burst
    return out


def wind():
    x = noise(12)
    base = band(x, 180, 900)
    gust = .55 + .45 * np.sin(2 * np.pi * (.11 * t(12) + .3 * np.sin(2 * np.pi * .05 * t(12))))
    whistle = np.sin(2 * np.pi * (640 + 60 * np.sin(2 * np.pi * .23 * t(12))) * t(12)) * (.5 + .5 * np.sin(2 * np.pi * .17 * t(12))) ** 3 * .05
    hiss = band(noise(12), 2500, 7000) * .08
    y = (base * gust * .9 + whistle * norm(base) + hiss * gust)
    save("wind_loop", norm(loopable(y, 1.5), .6))


def fire():
    rumble = band(noise(8), 40, 220) * (.7 + .3 * np.sin(2 * np.pi * .4 * t(8)))
    crackle = clicks(8, 14, 2200, .045, jitter=.9, strength=1.0) * .5 + clicks(8, 5, 900, .08, jitter=.8) * .35
    hiss = band(noise(8), 3000, 9000) * .06
    y = norm(rumble) * .55 + norm(crackle) * .6 + hiss
    save("fire_loop", norm(loopable(y, 1.0), .65))


def hum():
    k = t(2)
    y = np.zeros_like(k)
    for h, a in [(1, 1), (2, .5), (3, .32), (4, .2), (6, .12)]:
        y += a * np.sin(2 * np.pi * 70 * h * k)
    y += .18 * np.sin(2 * np.pi * 420 * k + 2 * np.sin(2 * np.pi * 70 * k))
    y += .06 * band(noise(2), 200, 1200)[:len(k)]
    save("lift_hum_loop", norm(y, .6))


def crank():
    y = clicks(1.0, 9, 1700, .05, jitter=.08) + .4 * clicks(1.0, 9, 420, .07, jitter=.08)
    save("crank_loop", norm(y, .7))


def scrape():
    n = band(noise(2), 700, 3800)
    m = .55 + .45 * np.sin(2 * np.pi * 7 * t(2) + 3 * np.sin(2 * np.pi * 1.3 * t(2)))
    y = n * m + .5 * band(noise(2), 120, 400)
    save("scrape_loop", norm(loopable(np.concatenate([y, y[:int(SR * .3)]]), .3), .6))


def roll():
    y = band(noise(2.3), 50, 260) * (.7 + .3 * np.sin(2 * np.pi * 5.5 * t(2.3)))
    y += .35 * band(noise(2.3), 600, 1400) * (.5 + .5 * np.sin(2 * np.pi * 11 * t(2.3)))
    save("roll_loop", norm(loopable(y, .3), .55))


def phone():
    k = t(1.8)
    ring = (np.sin(2 * np.pi * 1380 * k) + .7 * np.sin(2 * np.pi * 1130 * k) + .3 * np.sin(2 * np.pi * 2760 * k))
    ring *= (.5 + .5 * np.sign(np.sin(2 * np.pi * 22 * k))) * .5 + .5
    gate = ((k % .6) < .38).astype(float) * np.exp(-((k % .6)) * 1.5)
    gate *= (k < 1.2)
    save("phone_ring", norm(ring * gate, .6))


def paper():
    k = t(.38)
    y = band(noise(.38), 1800, 7500) * np.exp(-k * 7) * (1 + .6 * np.sin(2 * np.pi * 30 * k))
    y += .7 * band(noise(.38), 600, 2000) * np.exp(-((k - .12) ** 2) * 900)
    save("paper", norm(y, .5))


def pour():
    k = t(.9)
    y = clicks(.9, 90, 2600, .018, jitter=1.4) * np.linspace(1, .25, len(k))
    y += .6 * band(noise(.9), 250, 1400) * np.exp(-k * 2.2)
    save("pour", norm(y * env(len(k), .02, .25), .7))


def spill():
    k = t(.55)
    y = clicks(.55, 60, 2200, .02, jitter=1.4) * np.linspace(1, .2, len(k)) + .4 * band(noise(.55), 200, 1000) * np.exp(-k * 4)
    save("spill", norm(y * env(len(k), .01, .15), .55))


def whoosh(name, seconds, lo, hi, peak=.55):
    k = t(seconds)
    n = noise(seconds)
    y = np.zeros_like(k)
    for i, (a, b) in enumerate([(lo, lo * 2.2), (lo * 1.6, hi * .6), (hi * .5, hi)]):
        y += band(n, a, b) * np.sin(np.pi * np.clip((k / seconds + i * .1), 0, 1)) ** 2
    save(name, norm(y * env(len(k), .03, seconds * .5), peak))


def kindle():
    y = clicks(.3, 14, 3200, .02, jitter=.8) + .5 * band(noise(.3), 3500, 9000) * np.exp(-t(.3) * 25)
    save("kindle", norm(y, .55))


def ignite():
    k = t(1.5)
    sweep = np.sin(2 * np.pi * (50 + 55 * (1 - np.exp(-k * 4))) * k) * np.exp(-k * 1.6)
    whump = band(noise(1.5), 50, 400) * np.exp(-((k - .1) ** 2) * 14)
    tail = clicks(1.5, 22, 2400, .03, jitter=.9) * np.exp(-k * 1.4) * .4
    y = sweep * .9 + whump * 1.1 + tail
    save("ignite", norm(y * env(len(k), .005, .4), .8))


def wrench():
    y = clicks(.45, 12, 1100, .04, jitter=.1) + .5 * clicks(.45, 12, 3100, .02, jitter=.1)
    save("wrench", norm(y * env(len(y), .005, .1), .6))


def chain():
    y = clicks(.7, 16, 2500, .06, jitter=.5) * np.linspace(1, .4, int(SR * .7)) + .5 * clicks(.7, 7, 700, .08, jitter=.4)
    save("chain", norm(y * env(len(y), .005, .2), .6))


def blip():
    k = t(.05)
    y = np.sin(2 * np.pi * 520 * k) * np.exp(-k * 55) + .4 * np.sign(np.sin(2 * np.pi * 520 * k)) * np.exp(-k * 80)
    save("blip", norm(y * env(len(k), .002, .02), .5))


def drone():
    d = 24
    k = t(d)
    y = np.zeros_like(k)
    for f, a, r in [(55, 1, .05), (82.4, .55, .037), (110, .45, .03), (164.8, .22, .021), (65.4, .5, .043)]:
        y += a * np.sin(2 * np.pi * f * k + .8 * np.sin(2 * np.pi * r * k))
    y *= .6 + .4 * np.sin(2 * np.pi * .07 * k)
    y += .25 * band(noise(d), 60, 400) * (.5 + .5 * np.sin(2 * np.pi * .05 * k))
    save("drone_loop", norm(loopable(y, 3), .5))


def heartbeat():
    k = t(1.0)
    y = np.zeros_like(k)
    for at, a in [(.0, 1.0), (.26, .7)]:
        i = int(at * SR)
        L = int(.22 * SR)
        kk = np.arange(L) / SR
        y[i:i + L] += a * np.sin(2 * np.pi * (60 - 90 * kk) * kk) * np.exp(-kk * 22)
    save("heartbeat_loop", norm(y, .9))


def shiver():
    k = t(1.6)
    y = band(noise(1.6), 2800, 6500) * (.5 + .5 * np.sign(np.sin(2 * np.pi * 9.5 * k))) * (.6 + .4 * np.sin(2 * np.pi * .8 * k))
    save("shiver_loop", norm(loopable(np.concatenate([y, y[:int(SR * .2)]]), .2), .45))


def gong():
    k = t(3.5)
    y = np.zeros_like(k)
    for f, a, d in [(72, 1, 1.4), (108, .6, 1.8), (173, .35, 2.5), (261, .2, 3.0)]:
        y += a * np.sin(2 * np.pi * f * k + 1.2 * np.sin(2 * np.pi * (f * .5) * k) * np.exp(-k * 2)) * np.exp(-k / d)
    save("gong", norm(y * env(len(k), .005, .6), .8))


def groan():
    """The freight lift: old steel under load, a low sawtooth sagging in pitch, stick-slip creaks and the odd squeal."""
    d = 8
    k = t(d)
    f0 = 52 * (1 + .03 * np.sin(2 * np.pi * .21 * k) + .015 * np.sin(2 * np.pi * .57 * k))
    ph = 2 * np.pi * np.cumsum(f0) / SR
    body = sum(np.sin(h * ph) / h for h in range(1, 9))
    creak = band(noise(d), 280, 900) * (.5 + .5 * np.sin(2 * np.pi * .37 * k + 1.7 * np.sin(2 * np.pi * .11 * k))) ** 4
    squeal = np.sin(2 * np.pi * (410 + 60 * np.sin(2 * np.pi * .13 * k)) * k) * (.5 + .5 * np.sin(2 * np.pi * .19 * k)) ** 6
    y = norm(body) * .55 + norm(creak) * .9 + squeal * .18
    save("lift_groan_loop", norm(loopable(y, 1.0), .6))


def cart_roll():
    """Wheels on a concrete floor: a low rumble, a thunk at each slab joint and a faint squeak."""
    k = t(2)
    rumble = band(noise(2), 60, 300) * (.8 + .2 * np.sin(2 * np.pi * 3 * k))
    clack = clicks(2, 5, 260, .09, jitter=.04)
    squeak = np.sin(2 * np.pi * (1800 + 120 * np.sin(2 * np.pi * .7 * k)) * k) * (.5 + .5 * np.sin(2 * np.pi * 1.1 * k)) ** 8
    y = norm(rumble) * .6 + norm(clack) * .55 + squeak * .08
    save("cart_roll_loop", norm(y, .6))


def shovel():
    """A blade biting into coal: a gritty rush, small stones shifting, and a tink as it clears."""
    k = t(.5)
    grit = band(noise(.5), 900, 4500) * np.sin(np.pi * np.clip(k / .38, 0, 1)) ** 1.5 * (k < .38)
    stones = clicks(.5, 38, 700, .03, jitter=.9) * np.exp(-k * 5)
    tink = np.sin(2 * np.pi * 2400 * k) * np.exp(-np.clip(k - .3, 0, None) * 45) * (k > .3)
    y = norm(grit) * .8 + norm(stones) * .6 + tink * .35
    save("shovel", norm(y * env(len(k), .005, .08), .7))


def coal_patter():
    k = t(.35)
    y = clicks(.35, 26, 800, .035, jitter=1.0) * np.exp(-k * 7) + .3 * band(noise(.35), 300, 1500) * np.exp(-k * 14)
    save("coal_patter", norm(y, .6))


def bin_rattle():
    """The cart's steel bin taking a knock: a ring at a few resonances and loose coal rattling inside."""
    k = t(.6)
    ring = sum(a * np.sin(2 * np.pi * f * k) * np.exp(-k / d) for f, a, d in [(190, 1, .09), (310, .7, .07), (540, .5, .05), (880, .3, .03)])
    burst = band(noise(.6), 150, 1500) * np.exp(-k * 28)
    rattle = clicks(.6, 30, 1400, .02, jitter=1.0) * np.exp(-k * 6)
    y = norm(ring) * .6 + norm(burst) * .6 + norm(rattle) * .5
    save("bin_rattle", norm(y * env(len(k), .002, .1), .8))


def resonate(x, freq, q):
    """Noise through a band-pass whose centre moves (one frequency per sample): wind whistling round a corner."""
    w = 2 * np.pi * np.asarray(freq) / SR
    r = 1 - w / (2 * q)
    a1, a2 = list(2 * r * np.cos(w)), list(r * r)
    xs, g = list(x), list(1 - r)
    out = [0.0] * len(xs)
    y1 = y2 = 0.0
    for i in range(len(xs)):
        y0 = g[i] * xs[i] + a1[i] * y1 - a2[i] * y2
        out[i] = y0
        y2, y1 = y1, y0
    return np.array(out)


def howl():
    """The blizzard at its worst: a low roar, and the wind moaning round the building, rising and falling."""
    n = 16
    k = t(n)
    roar = band(noise(n), 40, 320)
    swell = .5 + .5 * np.sin(2 * np.pi * (.0625 * k + .35 * np.sin(2 * np.pi * .0625 * k)))
    moan = np.zeros_like(k)
    for f0, depth, rate, ph, amp in [(380, 110, .125, 0, 1), (560, 160, .1875, 1.7, .7), (260, 70, .0625, 3.1, .8)]:
        f = f0 + depth * np.sin(2 * np.pi * rate * k + ph)
        moan += norm(resonate(noise(n), f, 18)) * amp * (.4 + .6 * (.5 + .5 * np.sin(2 * np.pi * rate * 2 * k + ph)) ** 2)
    hiss = band(noise(n), 1800, 6000)
    y = norm(roar) * .7 * (.6 + .4 * swell) + norm(moan) * .55 * (.5 + .5 * swell) + norm(hiss) * .15 * swell
    save("howl_loop", norm(loopable(y, 2.0), .7))


def storm_knock():
    """Something loose banging on the building in the wind: a dull thump through the wall, sometimes twice."""
    for v in range(3):
        k = t(.9)
        y = np.zeros_like(k)
        hits = [(0, 1)] if v == 0 else [(0, 1), (rng.uniform(.14, .26), rng.uniform(.5, .8))]
        if v == 2:
            hits.append((hits[1][0] + rng.uniform(.12, .2), .4))
        for at, amp in hits:
            i = int(at * SR)
            m = k[: len(k) - i]
            f = rng.uniform(70, 95)
            thump = np.sin(2 * np.pi * f * m * (1 + .4 * np.exp(-m * 30))) * np.exp(-m * 16)
            body = np.sin(2 * np.pi * rng.uniform(190, 240) * m) * np.exp(-m * 30) * .4
            burst = band(rng.standard_normal(len(m)), 80, 600) * np.exp(-m * 60) * .5
            y[i:] += amp * (thump + body + norm(burst) * .5)
        save(f"storm_knock_{v}", norm(band(y, 30, 2500), .8))


def storm_rattle():
    """The window and the sheet metal round it rattling in a gust: quick ticks swelling and dying away."""
    for v in range(3):
        n = rng.uniform(.6, 1.2)
        k = t(n)
        ticks = clicks(n, rng.uniform(18, 32), rng.uniform(1100, 1900), .018, jitter=.8)
        shape = np.sin(np.pi * k / n) ** 1.5
        ring = band(noise(n), 2500, 5000) * .15
        save(f"storm_rattle_{v}", norm((norm(ticks) + ring) * shape * env(len(k), .01, .08), .7))


def relay():
    """A relay snapping shut: a hard little click, a dull thunk of the armature under it, and a short metal tick."""
    k = t(.12)
    snap = band(noise(.12), 2000, 7000) * np.exp(-k * 160)
    thunk = np.sin(2 * np.pi * 170 * k) * np.exp(-k * 45)
    tick = np.sin(2 * np.pi * 1850 * k) * np.exp(-k * 70) * .25
    save("relay", norm((snap + .6 * thunk + tick) * env(len(k), .0005, .03), .55))


def alarm():
    """The freeze alarm's "drrrt", kept soft: two low tones a fifth apart, each a sine with a little third harmonic (a rounded
    buzz, not a square wave), pulsed by a smooth gate with a slow attack so it never clicks or stabs."""
    k = t(.5)
    tone = lambda f: np.sin(2 * np.pi * f * k) + .25 * np.sin(2 * np.pi * 3 * f * k)
    buzz = tone(440) + .7 * tone(660)
    gate = .5 + .5 * np.sin(2 * np.pi * 22 * k - np.pi / 2)
    save("alarm", norm(buzz * gate * env(len(k), .03, .12), .6))


if __name__ == "__main__":
    for f in (wind, fire, hum, groan, cart_roll, shovel, coal_patter, bin_rattle, crank, scrape, roll, phone, paper, pour, spill, kindle, ignite, wrench, chain, blip, drone, heartbeat, shiver, gong):
        f()
    whoosh("dash", .4, 300, 2400)
    whoosh("lurch", .3, 200, 1200, .5)
    # New sounds go last, so the shared random stream leaves the older ones as they were.
    howl()
    storm_knock()
    storm_rattle()
    relay()
    alarm()
    print("audio written to", os.path.abspath(OUT), sorted(os.listdir(OUT)))
