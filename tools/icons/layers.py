# -*- coding: utf-8 -*-
"""Layers panel icons: Layer Manager (a palette), Layer Mapper, Layer Snapshot.

Family mark: a layer is a flat rhombus, and a stack of layers is that rhombus
with one or two chevrons under it, the edges of the layers beneath showing.
Ink draws the stack; the accent draws what the tool does: a docked palette bar
for the manager (hollow while closed, filled while open), an arrow for the
mapper, a camera with its lens and flash dots for the snapshot.

Each function takes make_icons.Ctx (0..96 space); c.small is the 32px pass.
"""


def _stack(c, cx, top, half_w, half_h, count, gap, w):
    """A rhombus with its top point at (cx, top), then count - 1 chevrons, each
    gap lower than the one above."""
    cy = top + half_h
    c.poly([(cx, top), (cx + half_w, cy), (cx, cy + half_h), (cx - half_w, cy)], w=w)
    for i in range(1, count):
        y = cy + gap * i
        c.line([(cx - half_w, y), (cx, y + half_h), (cx + half_w, y)], w=w)


def _arrow_right(c, x0, x1, y, w, head):
    """A horizontal accent arrow from x0 to its point at x1."""
    accent = c.t['accent']
    c.line([(x0, y), (x1 - head * 0.9, y)], color=accent, w=w)
    c.poly([(x1 - head, y - head * 0.75), (x1, y), (x1 - head, y + head * 0.75)],
           fill=accent, w=0)


def _manager(c, filled):
    """Three layers beside a docked palette bar."""
    if c.small:
        _stack(c, 34, 6, 30, 17, 2, 30, 12)
        c.line([(84, 10), (84, 86)], color=c.t['accent'], w=14)
    elif filled:
        _stack(c, 36, 14, 29, 14, 3, 17, 7)
        c.rrect(70, 10, 92, 86, rad=7, w=0, fill=c.t['accent'])
    else:
        _stack(c, 36, 14, 29, 14, 3, 17, 7)
        c.rrect(73, 13, 89, 83, rad=6, color=c.t['accent'], w=6)


def ic_layer_manager(c):
    """The palette closed: the bar is an accent outline."""
    _manager(c, filled=False)


def ic_layer_manager_on(c):
    """The palette open: the same bar, filled. Large only."""
    _manager(c, filled=True)


def ic_layer_mapper(c):
    """A stack of layers over an accent arrow: these layers become the standard."""
    if c.small:
        _stack(c, 40, 4, 32, 14, 2, 24, 12)
        _arrow_right(c, 8, 92, 78, 13, 30)
    else:
        _stack(c, 42, 8, 34, 15, 2, 18, 7)
        _arrow_right(c, 12, 90, 78, 8, 22)


def _camera(c, x0, y0, x1, y1, lens, flash):
    """A filled accent camera body with a top hump, the lens and flash punched
    out in paper."""
    accent, paper = c.t['accent'], c.t['paper']
    width = x1 - x0
    c.rrect(x0 + width * 0.18, y0 - 6, x0 + width * 0.48, y0 + 6, rad=3, w=0, fill=accent)
    c.rrect(x0, y0, x1, y1, rad=6, w=0, fill=accent)
    cx, cy = x0 + width * 0.5, (y0 + y1) / 2.0 + 1
    c.disc(cx, cy, lens, paper)
    c.disc(cx, cy, lens * 0.5, accent)
    if flash:
        c.disc(x1 - 7, y0 + 7, flash, paper)


def ic_layer_snapshot(c):
    """A stack of layers with an accent camera over its corner: keep this state."""
    if c.small:
        _stack(c, 34, 40, 30, 14, 2, 24, 12)
        _camera(c, 50, 10, 94, 42, 10, 0)
    else:
        _stack(c, 36, 38, 30, 14, 2, 17, 7)
        _camera(c, 54, 12, 92, 40, 9, 3)


ICONS = {
    'Layers.panel/Layer_Manager.dockpane': ic_layer_manager,
    'Layers.panel/Layer_Mapper.pushbutton': ic_layer_mapper,
    'Layers.panel/Layer_Snapshot.pushbutton': ic_layer_snapshot,
}

DOCKPANE_ON = {
    'Layers.panel/Layer_Manager.dockpane': ic_layer_manager_on,
}
