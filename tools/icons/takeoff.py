# -*- coding: utf-8 -*-
"""Takeoff panel icons: Length Takeoff and Tag Audit.

Same family as the Data panel: a block is a small ink rounded square with a dot
at its insertion point. A run (a duct or pipe) is an ink polyline with round
nodes at its ends. The accent is the action: a ruler's tick row under the run
for the length takeoff, a luggage tag hung off the block for the tag audit.

Each function takes make_icons.Ctx (0..96 space); c.small is the 32px pass.
"""


def _ruler(c, x0, x1, y, w, ticks, tall, short):
    """An accent ruler: a baseline with ticks rising from it, tall at both ends
    and the middle, short between."""
    col = c.t['accent']
    c.line([(x0, y), (x1, y)], color=col, w=w)
    for i in range(ticks):
        x = x0 + (x1 - x0) * i / float(ticks - 1)
        height = tall if i in (0, ticks - 1) or 2 * i == ticks - 1 else short
        c.line([(x, y), (x, y - height)], color=col, w=w)


def ic_length_takeoff(c):
    """An ink run with a bend in it over an accent ruler: measure the run."""
    if c.small:
        c.line([(8, 14), (46, 14), (46, 42), (88, 42)], w=12)
        _ruler(c, 8, 88, 86, 11, 3, 22, 22)
    else:
        run = [(10, 18), (46, 18), (46, 44), (86, 44)]
        c.line(run, w=7)
        for x, y in (run[0], run[-1]):
            c.disc(x, y, 6.5, c.t['ink'])
        _ruler(c, 10, 86, 84, 5, 5, 16, 9)


def _tag(c, x0, y0, x1, y1, hole, fill_hole):
    """A filled accent luggage tag pointing left, with its eyelet near the
    point punched out in paper."""
    col = c.t['accent']
    mid = (y0 + y1) / 2.0
    point = (y1 - y0) / 2.0
    c.poly([(x0, mid), (x0 + point, y0), (x1, y0), (x1, y1), (x0 + point, y1)],
           fill=col, w=0)
    if hole:
        c.disc(x0 + point * 0.9, mid, hole, fill_hole)


def ic_tag_audit(c):
    """A block with an accent tag hung off it on an ink string: check the
    equipment tags."""
    if c.small:
        c.rrect(4, 4, 44, 44, rad=8, w=0, fill=c.t['ink'])
        _tag(c, 34, 52, 92, 90, 0, None)
    else:
        c.rrect(6, 6, 42, 42, rad=7)
        c.disc(24, 24, 5.5, c.t['ink'])
        c.line([(36, 36), (49, 62)], w=4)
        _tag(c, 40, 50, 90, 76, 4.5, c.t['paper'])
        c.line([(64, 63), (82, 63)], color=c.t['paper'], w=4)


ICONS = {
    'Takeoff.panel/Length_Takeoff.pushbutton': ic_length_takeoff,
    'Takeoff.panel/Tag_Audit.pushbutton': ic_tag_audit,
}
