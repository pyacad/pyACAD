"""Cleanup panel icons: Drawing Cleanup and Font and Proxy Report.

Family language: the broom is the thing (ink), its sweep is the action
(accent arc); the font report is an ink "A" with an accent question mark,
the font that cannot be found. Alert red stays reserved for Purge.
"""
import math


def _along(origin, direction, distance, perp=None, offset=0.0):
    """origin + distance * direction (+ offset * perp), as an (x, y) pair."""
    x = origin[0] + direction[0] * distance
    y = origin[1] + direction[1] * distance
    if perp is not None:
        x += perp[0] * offset
        y += perp[1] * offset
    return (x, y)


def _broom(c, top, neck, length, neck_half, tip_half, bristles, w_handle, w_bristle):
    """A broom: handle from top to neck, a solid ferrule, then bristles
    fanning out over length. bristles=0 draws the head as one solid fan,
    which is what still reads as a broom at 32px."""
    dx, dy = neck[0] - top[0], neck[1] - top[1]
    norm = math.hypot(dx, dy)
    d = (dx / norm, dy / norm)
    p = (d[1], -d[0])                                   # perpendicular
    c.line([top, neck], w=w_handle)
    if not bristles:
        c.poly([_along(neck, d, 0, p, neck_half), _along(neck, d, 0, p, -neck_half),
                _along(neck, d, length, p, -tip_half), _along(neck, d, length, p, tip_half)],
               fill=c.t['ink'], w=0)
        return
    band = length * 0.3
    c.poly([_along(neck, d, 0, p, neck_half), _along(neck, d, 0, p, -neck_half),
            _along(neck, d, band, p, -neck_half * 1.2), _along(neck, d, band, p, neck_half * 1.2)],
           fill=c.t['ink'], w=0)
    for i in range(bristles):
        t = -1.0 + 2.0 * i / (bristles - 1)
        start = _along(neck, d, band, p, t * neck_half)
        end = _along(neck, d, length, p, t * tip_half)
        c.line([start, end], w=w_bristle)


def _sweep(c, cx, cy, r, start, end, w):
    """The accent sweep: an arc the broom has just swept along."""
    S = c.img.width // 96
    c.d.arc([(cx - r) * S, (cy - r) * S, (cx + r) * S, (cy + r) * S],
            start=start, end=end, fill=c.t['accent'], width=int(w * S))
    for angle in (start, end):                          # round caps
        a = math.radians(angle)
        c.disc(cx + r * math.cos(a), cy + r * math.sin(a), w / 2.0, c.t['accent'])


def ic_drawing_cleanup(c):
    """A broom in ink sweeping an accent arc: one pass that tidies the drawing."""
    if c.small:
        _broom(c, (90, 6), (62, 34), 38, 9, 22, 0, 13, 0)
        _sweep(c, 40, 18, 70, 66, 128, 13)
    else:
        _broom(c, (88, 6), (62, 32), 38, 9, 20, 7, 7, 5)
        _sweep(c, 40, 24, 64, 62, 128, 7)
        c.disc(80, 72, 4, c.t['accent'])
        c.disc(88, 62, 3, c.t['accent'])


def _question(c, cx, cy, r, stem, dot, w):
    """An accent question mark: hook of radius r centred on (cx, cy), a stem
    down to stem, a dot at dot (y)."""
    S = c.img.width // 96
    c.d.arc([(cx - r) * S, (cy - r) * S, (cx + r) * S, (cy + r) * S],
            start=190, end=400, fill=c.t['accent'], width=int(w * S))
    a = math.radians(190)
    c.disc(cx + r * math.cos(a), cy + r * math.sin(a), w / 2.0, c.t['accent'])
    a = math.radians(400)
    hook_end = (cx + r * math.cos(a), cy + r * math.sin(a))
    c.line([hook_end, (cx, cy + r * 1.1), (cx, stem)], color=c.t['accent'], w=w)
    c.disc(cx, dot, w * 0.75, c.t['accent'])


def ic_font_proxy_report(c):
    """An ink "A" beside an accent question mark: a font AutoCAD cannot find."""
    if c.small:
        c.line([(4, 90), (30, 10), (56, 90)], w=13)
        c.line([(16, 62), (44, 62)], w=12)
        _question(c, 76, 26, 13, 58, 82, 11)
    else:
        c.line([(8, 86), (32, 12), (56, 86)], w=8)
        c.line([(18, 60), (46, 60)], w=7)
        _question(c, 74, 28, 12, 54, 70, 7)


ICONS = {
    'Cleanup.panel/Drawing_Cleanup.pushbutton': ic_drawing_cleanup,
    'Cleanup.panel/Font_Proxy_Report.pushbutton': ic_font_proxy_report,
}
