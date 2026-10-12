# -*- coding: utf-8 -*-
"""Xrefs panel icons for tools/make_icons.py.

Family: an xref is a sheet in ink (a rounded rectangle) with a chain link in its
lower right, the reference to another file. The action sits on the sheet's top
right corner as an accent badge, cut out of the sheet so the two never touch:
two circling arrows for the manager (reload, unload, detach, bind), a folder
with an arrow into it for repath. At 32px a chain link turns to hatching, so
the sheet shrinks, the link becomes a solid corner mark and the badge grows.
Every function takes the make_icons Ctx; nothing is imported from it.
"""
import math

CLEAR = (0, 0, 0, 0)        # erases to transparent: cut-outs and hollow links


def _scale(c):
    """The Ctx supersampling factor (its 0..96 space is drawn larger)."""
    return c.img.size[0] / 96.0


def _link(c, cx, cy, half, width, stroke):
    """One chain link: a hollow capsule on the 45 degree diagonal, centred at
    cx, cy, half the distance between its round ends long."""
    d = half / math.sqrt(2)
    ends = [(cx - d, cy + d), (cx + d, cy - d)]
    c.line(ends, w=width)
    c.line(ends, color=CLEAR, w=width - 2 * stroke)


def _xref(c):
    """The sheet with its link mark, drawn before the badge cuts in."""
    if c.small:
        c.rrect(6, 30, 62, 90, rad=10, w=12)
        c.poly([(54, 60), (54, 82), (32, 82)], fill=c.t['ink'], w=0)
    else:
        c.rrect(8, 18, 66, 88, rad=8, w=7)
        _link(c, 34, 69, 4.5, 17, 4.5)
        _link(c, 48, 55, 4.5, 17, 4.5)


def _cut(c, cx, cy, r):
    """Clears a disc so the badge reads in front of the sheet."""
    c.disc(cx, cy, r, CLEAR)


def _arc_arrow(c, cx, cy, r, start, end, w, head):
    """A clockwise arc from start to end degrees (0 is 3 o'clock, y down) with
    an arrowhead at end pointing the way it travels."""
    col = c.t['accent']
    s = _scale(c)
    c.d.arc([(cx - r) * s, (cy - r) * s, (cx + r) * s, (cy + r) * s],
            start=start, end=end - 8, fill=col, width=int(w * s))
    a = math.radians(end)
    ex, ey = cx + r * math.cos(a), cy + r * math.sin(a)
    tx, ty = -math.sin(a), math.cos(a)          # travel direction
    nx, ny = math.cos(a), math.sin(a)           # outward
    back = head * 0.35
    c.poly([(ex + tx * head * 0.75, ey + ty * head * 0.75),
            (ex - tx * back + nx * head * 0.62, ey - ty * back + ny * head * 0.62),
            (ex - tx * back - nx * head * 0.62, ey - ty * back - ny * head * 0.62)],
           fill=col, w=0)


def ic_xref_manager(c):
    """The xref sheet with two circling accent arrows on its corner: reload,
    unload, detach or bind the drawing's xrefs."""
    _xref(c)
    if c.small:
        _cut(c, 68, 26, 30)
        _arc_arrow(c, 68, 26, 20, 200, 330, 10, 20)
        _arc_arrow(c, 68, 26, 20, 20, 150, 10, 20)
    else:
        _cut(c, 70, 26, 26)
        _arc_arrow(c, 70, 26, 17, 200, 335, 6.5, 14)
        _arc_arrow(c, 70, 26, 17, 20, 155, 6.5, 14)


def _folder(c, x0, y0, x1, y1, tab):
    """A filled accent folder: body plus the tab on its top left."""
    col = c.t['accent']
    c.poly([(x0, y0), (x0 + tab, y0), (x0 + tab + 5, y0 + 6), (x0, y0 + 6)], fill=col, w=0)
    c.rrect(x0, y0 + 5, x1, y1, rad=4, w=0, fill=col)


def ic_xref_repath(c):
    """The xref sheet with an accent folder on its corner and an arrow into
    it: point the xrefs at a new place."""
    _xref(c)
    if c.small:
        _cut(c, 68, 26, 31)
        _folder(c, 42, 4, 94, 48, 22)
        c.line([(52, 30), (72, 30)], color=CLEAR, w=9)
        c.poly([(70, 18), (86, 30), (70, 42)], fill=CLEAR, w=0)
    else:
        _cut(c, 69, 27, 26)
        _folder(c, 48, 10, 90, 44, 16)
        c.line([(56, 29), (74, 29)], color=CLEAR, w=5)
        c.poly([(72, 21), (82, 29), (72, 37)], fill=CLEAR, w=0)


ICONS = {
    'Xrefs.panel/Xref_Manager.pushbutton': ic_xref_manager,
    'Xrefs.panel/Xref_Repath.pushbutton': ic_xref_repath,
}
