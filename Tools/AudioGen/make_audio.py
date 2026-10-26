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
        burst = (np.sin(2 * np.pi * f * k) + .5 * noise(L / SR)[:L]) * np.exp(-k / (decay * .22)) * rng.uniform(.4, 1) * strength
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


if __name__ == "__main__":
    for f in (wind, fire, hum, crank, scrape, roll, phone, paper, pour, spill, kindle, ignite, wrench, chain, blip, drone, heartbeat, shiver, gong):
        f()
    whoosh("dash", .4, 300, 2400)
    whoosh("lurch", .3, 200, 1200, .5)
    print("audio written to", os.path.abspath(OUT), sorted(os.listdir(OUT)))
