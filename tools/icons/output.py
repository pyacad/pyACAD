# -*- coding: utf-8 -*-
"""Output panel icons: Batch Plot and Layout Manager.

Family mark: the sheet, a page with its top right corner folded, drawn in ink
as the thing both tools work on. The accent says what is done to it: a second
sheet stacked behind and an arrow leaving for Batch Plot (many sheets, out to
files), a row of tabs along the bottom edge for Layout Manager (the layout
tabs themselves).
"""


def _sheet(c, x0, y0, x1, y1, fold, color=None, w=None):
    """A page outline with its top right corner folded over."""
    c.poly([(x0, y0), (x1 - fold, y0), (x1, y0 + fold), (x1, y1), (x0, y1)], color=color, w=w)
    c.line([(x1 - fold, y0), (x1 - fold, y0 + fold), (x1, y0 + fold)], color=color, w=w)


def ic_batch_plot(c):
    """Ink sheet in front, the accent edge of a second sheet behind it, and an
    accent arrow heading down through the front sheet: many layouts out to PDF."""
    accent = c.t['accent']
    if c.small:
        c.line([(8, 58), (8, 6), (58, 6)], color=accent, w=12)
        _sheet(c, 24, 22, 90, 92, 22, w=12)
        c.arrow_v(54, 42, 82, w=11, head=22)
    else:
        c.line([(10, 62), (10, 8), (56, 8)], color=accent, w=7)
        _sheet(c, 24, 20, 84, 90, 18)
        c.arrow_v(52, 40, 78, w=8, head=18)


def ic_layout_manager(c):
    """Ink sheet with accent tabs along its bottom edge, like the layout tabs
    under the drawing area."""
    accent = c.t['accent']
    if c.small:
        _sheet(c, 10, 6, 86, 62, 22, w=12)
        c.rrect(8, 72, 44, 92, rad=4, w=0, fill=accent)
        c.rrect(52, 72, 88, 92, rad=4, w=0, fill=accent)
    else:
        _sheet(c, 12, 8, 84, 64, 18)
        for x0 in (12, 38, 64):
            c.rrect(x0, 74, x0 + 20, 88, rad=3, w=0, fill=accent)


ICONS = {
    'Output.panel/Batch_Plot.pushbutton': ic_batch_plot,
    'Output.panel/Layout_Manager.pushbutton': ic_layout_manager,
}
