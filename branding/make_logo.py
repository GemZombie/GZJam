"""Writes GZJam's logo files (SVG) into this folder.

Everything is built from the same few shapes, so a change here reaches every file:
the waveform G (the icon), and the GZJAM wordmark, whose letters are cut into bars like a waveform.
GZ is coloured as the played part of the waveform, JAM as the part still to come.

    python branding/make_logo.py
"""

import math
from pathlib import Path

OUT = Path(__file__).parent

# Colours. Cyan is Deck 1's colour in the app; the deep cyan is the same hue, readable on white.
DARK = {"lit": "#36C2DC", "unlit": "#C3CBD6", "ink": "#E8ECF2"}
LIGHT = {"lit": "#1597B0", "unlit": "#2A303B", "ink": "#151920"}

# ---- The waveform G, in a 64-unit square ----

def wave_g(lit, unlit):
    """Bars mirrored around a ring that opens into a G, and the G's bar."""
    cx = cy = 32
    radius, step, last = 18, 9, 306
    parts = []
    for deg in range(2 * step, last + 1, step):
        i = deg / step
        envelope = 0.55 + 0.45 * math.sin(i * 0.42 + 0.6) ** 2
        h = 1.6 + 5.2 * envelope * [1, 0.72, 0.9, 0.62][int(i) % 4]
        a = math.radians(deg)
        c, s = math.cos(a), math.sin(a)
        colour = lit if deg <= 160 else unlit
        parts.append(
            f'<line x1="{cx + (radius - h) * c:.2f}" y1="{cy + (radius - h) * s:.2f}" '
            f'x2="{cx + (radius + h) * c:.2f}" y2="{cy + (radius + h) * s:.2f}" stroke="{colour}" stroke-width="1.9"/>')
    parts.append(f'<line x1="33" y1="32" x2="{cx + radius + 5}" y2="32" stroke="{lit}" stroke-width="3.4"/>')
    return "\n    ".join(parts)

# The bars reach from about 7.2 to 56.8 each way; the G's bar ends at 55.
MARK_BOX = (6.2, 6.2, 51.6, 51.6)

TILE_DEFS = """<linearGradient id="tile" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#1E232C"/><stop offset="1" stop-color="#0C0F14"/>
    </linearGradient>"""
TILE = '<rect x="1" y="1" width="62" height="62" rx="13" fill="url(#tile)" stroke="#2B313C"/>'

# ---- The wordmark, on a 20-unit cap height ----

STROKE = 4.4
GLYPHS = {
    # Strokes are 4.4 wide; Z and M are filled shapes so their sharp corners stay sharp.
    "G": {"stroke": "M15.01,4.02 A7.8,7.8 0 1 0 17.8,10 M17.8,10 H11"},
    "Z": {"fill": "M2.5,0 H17.8 V4 L7.6,15.6 H17.8 V20 H2.2 V16 L12.4,4.4 H2.5 Z"},
    "J": {"stroke": "M15.6,0 V12 A5.6,5.6 0 0 1 4.4,12"},
    "A": {"stroke": "M2.2,20 V10 A7.8,7.8 0 0 1 17.8,10 V20 M2.2,13.6 H17.8"},
    "M": {"fill": "M0,20 V0 H4.6 L10,8.6 L15.4,0 H20 V20 H15.6 V7.6 L10,16.6 L4.4,7.6 V20 Z"},
}
# Origins, spaced by the letters' real edges (G, A, M: 0-20; Z, J: 2.2-17.8). J tucks in under Z,
# since its top is open.
ORIGINS = {"G": 0, "Z": 21.2, "J": 38.6, "A": 59.8, "M": 83.4}
WORD_WIDTH = 103.4
# The pitch is chosen so the word starts and ends on a whole bar (43 bars across).
BAR_WIDTH = 1.65
BAR_PITCH = (WORD_WIDTH - BAR_WIDTH) / 42

def letter(ch, colour):
    g = GLYPHS[ch]
    body = ""
    if "stroke" in g:
        body += f'<path d="{g["stroke"]}" fill="none" stroke="{colour}" stroke-width="{STROKE}"/>'
    if "fill" in g:
        body += f'<path d="{g["fill"]}" fill="{colour}"/>'
    return f'<g transform="translate({ORIGINS[ch]} 0)">{body}</g>'

def word_letters(theme, solid):
    if solid:
        return "".join(letter(ch, theme["ink"]) for ch in "GZJAM")
    return "".join(letter(ch, theme["lit"] if ch in "GZ" else theme["unlit"]) for ch in "GZJAM")

def bar_clip(clip_id):
    n = round((WORD_WIDTH - BAR_WIDTH) / BAR_PITCH) + 1
    rects = "".join(f'<rect x="{i * BAR_PITCH:.2f}" y="-1" width="{BAR_WIDTH}" height="22"/>' for i in range(n))
    return f'<clipPath id="{clip_id}">{rects}</clipPath>'

def wordmark_group(theme, solid, clip_id="bars"):
    if solid:
        return f"<g>{word_letters(theme, True)}</g>", ""
    return f'<g clip-path="url(#{clip_id})">{word_letters(theme, False)}</g>', bar_clip(clip_id)

# ---- Files ----

def svg(view_box, body, defs="", title="GZJam"):
    x, y, w, h = view_box
    defs_block = f"\n  <defs>\n    {defs}\n  </defs>" if defs else ""
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="{x:g} {y:g} {w:g} {h:g}" '
            f'width="{w:g}" height="{h:g}">\n  <title>{title}</title>{defs_block}\n  {body}\n</svg>\n')

def icon():
    return svg((0, 0, 64, 64), f"{TILE}\n  <g>\n    {wave_g(DARK['lit'], DARK['unlit'])}\n  </g>", TILE_DEFS)

def mark(theme):
    return svg(MARK_BOX, f"<g>\n    {wave_g(theme['lit'], theme['unlit'])}\n  </g>")

def wordmark(theme, solid):
    group, defs = wordmark_group(theme, solid)
    return svg((-1, -1, WORD_WIDTH + 2, 22), group, defs)

def logo(theme):
    """The icon beside the wordmark: cap height 0.41 of the icon, centred on it."""
    scale = 26 / 20
    gap = 19
    group, defs = wordmark_group(theme, solid=False)
    body = (f"{TILE}\n  <g>\n    {wave_g(DARK['lit'], DARK['unlit'])}\n  </g>\n"
            f'  <g transform="translate({64 + gap} 19) scale({scale})">{group}</g>')
    return svg((0, 0, 64 + gap + WORD_WIDTH * scale, 64), body, TILE_DEFS + "\n    " + defs)

FILES = {
    "gzjam-icon.svg": icon(),
    "gzjam-mark.svg": mark(DARK),
    "gzjam-mark-light.svg": mark(LIGHT),
    "gzjam-wordmark.svg": wordmark(DARK, solid=False),
    "gzjam-wordmark-light.svg": wordmark(LIGHT, solid=False),
    "gzjam-wordmark-solid.svg": wordmark(DARK, solid=True),
    "gzjam-wordmark-solid-light.svg": wordmark(LIGHT, solid=True),
    "gzjam-logo.svg": logo(DARK),
    "gzjam-logo-light.svg": logo(LIGHT),
}

if __name__ == "__main__":
    for name, text in FILES.items():
        (OUT / name).write_text(text, encoding="utf-8", newline="\n")
        print(name)
