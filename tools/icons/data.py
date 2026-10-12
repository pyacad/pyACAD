# -*- coding: utf-8 -*-
"""Data panel icons: Blocks to Excel, Excel to Blocks, Block Schedule and
Find and Replace Attributes.

Family marks, both in ink: a block is a small rounded square with a dot at its
insertion point; a sheet (a workbook, a table) is a rounded rectangle ruled
with two lines. The accent is the action: an elbow arrow carrying one into the
other for the export and the import (always from the top left thing to the
bottom right one, so the two read as a pair), a header band for the schedule,
a magnifier for find and replace.

Each function takes make_icons.Ctx (0..96 space); c.small is the 32px pass,
which drops the rules and the dot and fills the block so the shapes survive.
"""


def _block(c, x0, y0, x1, y1, w=None, dot=5.5, rad=7):
    """A block: an ink rounded square with a dot at its insertion point. At
    32px a solid square reads better, so pass dot=None and fill it."""
    if dot is None:
        c.rrect(x0, y0, x1, y1, rad=rad, w=0, fill=c.t['ink'])
        return
    c.rrect(x0, y0, x1, y1, rad=rad, w=w)
    c.disc((x0 + x1) / 2.0, (y0 + y1) / 2.0, dot, c.t['ink'])


def _sheet(c, x0, y0, x1, y1, w=None, rules=2, rule_w=6, inset=10, rad=6):
    """A sheet: an ink rounded rectangle ruled with horizontal lines."""
    c.rrect(x0, y0, x1, y1, rad=rad, w=w)
    step = (y1 - y0) / float(rules + 1)
    for i in range(1, rules + 1):
        y = y0 + step * i
        c.line([(x0 + inset, y), (x1 - inset, y)], w=rule_w)


def _elbow(c, x0, y, x1, y1, w, head):
    """An accent arrow running right from (x0, y) to x1, then down to its point
    at (x1, y1): one thing carried into the other."""
    col = c.t['accent']
    c.line([(x0, y), (x1, y), (x1, y1 - head * 0.9)], color=col, w=w)
    c.poly([(x1 - head * 0.65, y1 - head), (x1 + head * 0.65, y1 - head), (x1, y1)],
           fill=col, w=0)


def ic_blocks_to_excel(c):
    """A block, an accent elbow arrow, a sheet: blocks out to a workbook."""
    if c.small:
        _block(c, 4, 4, 40, 40, dot=None, rad=8)
        _elbow(c, 44, 22, 72, 50, 12, 22)
        _sheet(c, 42, 52, 92, 92, w=12, rules=1, rule_w=10, inset=14)
    else:
        _block(c, 6, 8, 40, 42, w=7)
        _elbow(c, 46, 25, 68, 50, 7, 15)
        _sheet(c, 38, 52, 92, 90, w=7, rules=2, rule_w=6, inset=11)


def ic_excel_to_blocks(c):
    """The same pair the other way round: a sheet, the arrow, a block."""
    if c.small:
        _sheet(c, 4, 4, 52, 44, w=12, rules=1, rule_w=10, inset=14)
        _elbow(c, 56, 22, 72, 52, 12, 22)
        _block(c, 54, 54, 90, 90, dot=None, rad=8)
    else:
        _sheet(c, 4, 8, 58, 46, w=7, rules=2, rule_w=6, inset=11)
        _elbow(c, 62, 25, 72, 52, 7, 15)
        _block(c, 52, 54, 88, 90, w=7)


def ic_block_schedule(c):
    """A table in ink with an accent header band and a block dot on each row:
    one line per block, counted."""
    ink, accent = c.t['ink'], c.t['accent']
    if c.small:
        c.rrect(6, 6, 90, 34, rad=8, w=0, fill=accent)
        c.rrect(6, 6, 90, 90, rad=8, w=12)
        c.line([(12, 62), (84, 62)], w=11)          # inset: round caps stay inside
        c.line([(42, 36), (42, 84)], w=11)
    else:
        c.rrect(8, 10, 88, 30, rad=6, w=0, fill=accent)
        c.rrect(8, 10, 88, 88, rad=6, w=7)
        for y in (50, 69):
            c.line([(11, y), (85, y)], w=5)
        c.line([(38, 30), (38, 85)], w=5)
        for y in (40, 59.5, 78.5):
            c.disc(23, y, 4.5, ink)
            c.line([(48, y), (76, y)], w=5)


def _magnifier(c, cx, cy, r, w, handle_w, handle):
    """An accent magnifier: a ring and a handle off its lower right."""
    col = c.t['accent']
    c.circle(cx, cy, r, color=col, w=w)
    k = 0.7071
    start = (cx + (r + w * 0.3) * k, cy + (r + w * 0.3) * k)
    c.line([start, (start[0] + handle * k, start[1] + handle * k)], color=col, w=handle_w)


def ic_find_replace_attributes(c):
    """A block with its attribute lines under it, and an accent magnifier over
    them: the values being searched."""
    if c.small:
        _block(c, 4, 4, 44, 44, dot=None, rad=8)
        c.line([(6, 66), (30, 66)], w=12)
        _magnifier(c, 60, 58, 19, 11, 14, 20)
    else:
        _block(c, 6, 6, 42, 42, w=7)
        c.line([(8, 58), (40, 58)], w=6)
        c.line([(8, 74), (30, 74)], w=6)
        _magnifier(c, 62, 60, 17, 7, 9, 16)


ICONS = {
    'Data.panel/Blocks_to_Excel.pushbutton': ic_blocks_to_excel,
    'Data.panel/Excel_to_Blocks.pushbutton': ic_excel_to_blocks,
    'Data.panel/Block_Schedule.pushbutton': ic_block_schedule,
    'Data.panel/Find_Replace_Attributes.pushbutton': ic_find_replace_attributes,
}
