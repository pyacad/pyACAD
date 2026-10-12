# -*- coding: utf-8 -*-
"""Regenerates every ribbon icon for the pyACAD extension.

Run with any CPython that has Pillow:

    python tools/make_icons.py
    python tools/make_icons.py --sheet out.png     (contact sheet for review)

This is a dev-time tool. It never ships into AutoCAD and is never loaded by
an engine, so it may use modern CPython syntax.

Design language:
  * neutral ink draws the THING, a solid accent mark draws the ACTION, so colour
    always means "what this tool does"
  * 0..96 coordinate space, drawn at 4x and downsampled; one stroke weight per
    density, rounded caps
  * shared family marks carry cohesion: the open tray is the memory register and
    appears in eight Memory glyphs; the spark marks a collision
  * alert red appears exactly ONCE in the whole set, on Purge, because it is the
    only destructive tool. Do not add a second.
  * four files per bundle: icon.png / icon.dark.png at 96px for large buttons,
    icon.small.png / icon.small.dark.png at 32px for stacked and menu buttons.
    The small files are separate, simplified drawings, not resized copies.

Never hand-edit an icon. Change the drawing here and re-run.
"""
import argparse
import math
import os

from PIL import Image, ImageDraw

S = 4
SIZE = 96 * S
LARGE_PX = 96
SMALL_PX = 32

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TAB = os.path.join(ROOT, 'extensions', 'pyACAD.extension', 'pyACAD.tab')
SMOKE_TAB = os.path.join(ROOT, 'tests', 'fixtures', 'Smoke.extension', 'Smoke.tab')

LIGHT = {'ink': (61, 68, 81, 255), 'accent': (0, 120, 212, 255),
         'alert': (196, 43, 28, 255), 'paper': (255, 255, 255, 255)}
DARK = {'ink': (232, 232, 232, 255), 'accent': (76, 194, 255, 255),
        'alert': (255, 95, 82, 255), 'paper': (32, 32, 32, 255)}

THEMES = [('', LIGHT), ('.dark', DARK)]
WEIGHT_LARGE = 8
WEIGHT_SMALL = 14


class Ctx(object):
    """Drawing helper working in a 0..96 coordinate space."""

    def __init__(self, theme, weight):
        self.img = Image.new('RGBA', (SIZE, SIZE), (0, 0, 0, 0))
        self.d = ImageDraw.Draw(self.img)
        self.t = theme
        self.w = int(weight * S)
        self.small = weight >= WEIGHT_SMALL

    def _xy(self, pts):
        return [(x * S, y * S) for x, y in pts]

    def line(self, pts, color=None, w=None):
        c = color or self.t['ink']
        w = int(w * S) if w else self.w
        p = self._xy(pts)
        self.d.line(p, fill=c, width=w, joint='curve')
        r = w / 2.0
        for x, y in p:                                  # round caps
            self.d.ellipse([x - r, y - r, x + r, y + r], fill=c)

    def circle(self, cx, cy, r, color=None, w=None, fill=None):
        w = int(w * S) if w is not None else self.w
        box = [(cx - r) * S, (cy - r) * S, (cx + r) * S, (cy + r) * S]
        self.d.ellipse(box, outline=(color or self.t['ink']) if w else None,
                       width=w, fill=fill)

    def disc(self, cx, cy, r, color):
        self.d.ellipse([(cx - r) * S, (cy - r) * S, (cx + r) * S, (cy + r) * S], fill=color)

    def rrect(self, x0, y0, x1, y1, rad=8, color=None, w=None, fill=None):
        w = int(w * S) if w is not None else self.w
        self.d.rounded_rectangle([x0 * S, y0 * S, x1 * S, y1 * S], radius=rad * S,
                                 outline=(color or self.t['ink']) if w else None,
                                 width=w, fill=fill)

    def poly(self, pts, color=None, fill=None, w=None):
        p = self._xy(pts)
        if fill:
            self.d.polygon(p, fill=fill)
        if w != 0:
            ww = int(w * S) if w else self.w
            closed = p + [p[0]]
            self.d.line(closed, fill=color or self.t['ink'], width=ww, joint='curve')
            r = ww / 2.0
            for x, y in closed:
                self.d.ellipse([x - r, y - r, x + r, y + r], fill=color or self.t['ink'])

    def arrow_v(self, x, y0, y1, color=None, w=None, head=16):
        c = color or self.t['accent']
        d = 1 if y1 > y0 else -1
        end = y1 - d * head * 0.9
        self.line([(x, y0), (x, end)], color=c, w=w)
        self.poly([(x - head * 0.6, end), (x + head * 0.6, end), (x, y1)],
                  color=c, fill=c, w=0)

    def png(self, px):
        return self.img.resize((px, px), Image.LANCZOS)


# --- General panel -----------------------------------------------------------

def ic_console(c):
    if c.small:
        c.line([(14, 24), (44, 48), (14, 72)], color=c.t['accent'], w=15)
        c.line([(56, 74), (88, 74)], w=15)
    else:
        c.rrect(8, 16, 88, 80, rad=10)
        c.line([(26, 36), (46, 48), (26, 60)], color=c.t['accent'], w=9)
        c.line([(54, 62), (74, 62)], w=8)


def ic_reload(c):
    """Circular arrow with a gap at the top right, head tangent to the arc."""
    r = 32
    w = 15 if c.small else 9
    c.d.arc([(48 - r) * S, (48 - r) * S, (48 + r) * S, (48 + r) * S],
            start=-50, end=200, fill=c.t['accent'], width=int(w * S))
    hx, hy = 48 + r * 0.64, 48 - r * 0.77
    size = 17 if c.small else 12
    c.poly([(hx - size * 0.2, hy - size), (hx + size, hy + size * 0.25),
            (hx - size * 0.55, hy + size * 0.6)],
           color=c.t['accent'], fill=c.t['accent'], w=0)


def ic_settings(c):
    """Two sliders with accent knobs: the pyACAD settings are a handful of
    switches, not a machine, and a slider row still reads at 16px where a gear
    turns to mush."""
    if c.small:
        for y, knob in ((34, 64), (68, 34)):
            c.line([(10, y), (86, y)], w=12)
            c.disc(knob, y, 15, c.t['accent'])
    else:
        for y, knob in ((36, 62), (66, 36)):
            c.line([(14, y), (82, y)], w=8)
            c.disc(knob, y, 12, c.t['accent'])


def ic_shortcuts(c):
    """Keycaps; the accent cap is the one being bound."""
    if c.small:
        c.rrect(8, 10, 88, 70, rad=12, w=13)
        c.line([(28, 88), (68, 88)], color=c.t['accent'], w=15)
    else:
        c.rrect(8, 18, 44, 54, rad=8)
        c.rrect(52, 18, 88, 54, rad=8)
        c.rrect(30, 62, 66, 92, rad=8, w=0, fill=c.t['accent'])


# --- AI panel -----------------------------------------------------------------

def sparkle(c, cx, cy, r, color=None):
    """Four-point star: the mark of a thing the assistant made."""
    col = color or c.t['accent']
    k = r * 0.3
    pts = [(cx, cy - r), (cx + k, cy - k), (cx + r, cy), (cx + k, cy + k),
           (cx, cy + r), (cx - k, cy + k), (cx - r, cy), (cx - k, cy - k)]
    c.poly(pts, fill=col, w=0)


def _bubble(c, fill=None, w=None):
    """A speech bubble: rounded body with a tail at the lower left."""
    if fill:
        c.rrect(8, 10, 88, 66, rad=14, fill=fill, w=0)
        c.poly([(24, 62), (24, 88), (46, 62)], fill=fill, w=0)
    else:
        c.rrect(8, 10, 88, 66, rad=14, w=w)
        c.line([(24, 64), (24, 88), (46, 64)], w=w)


def ic_ask_ai(c):
    """A speech bubble in ink with an accent sparkle inside: a chat that makes
    tools. This is the ribbon toggle's OFF art, the panel closed."""
    if c.small:
        _bubble(c, w=12)
        sparkle(c, 48, 38, 18)
    else:
        _bubble(c, w=7)
        sparkle(c, 48, 38, 15)


def ic_ask_ai_on(c):
    """The same bubble filled with accent, the sparkle punched out in paper:
    the ribbon toggle's ON art while the panel is open. Large only."""
    _bubble(c, fill=c.t['accent'])
    sparkle(c, 48, 38, 15, color=c.t['paper'])


def ic_ai_settings(c):
    """A key in ink, the bow left, teeth right, with the accent sparkle over the
    bow: the API key that lets the assistant in."""
    if c.small:
        c.circle(28, 52, 16, w=12)
        c.line([(44, 52), (90, 52)], w=12)
        c.line([(74, 52), (74, 70)], w=12)
        sparkle(c, 28, 52, 9)
    else:
        c.circle(28, 52, 14, w=7)
        c.line([(42, 52), (90, 52)], w=7)
        c.line([(70, 52), (70, 68)], w=7)
        c.line([(84, 52), (84, 66)], w=7)
        sparkle(c, 28, 52, 8)


def ic_open_ai_folder(c):
    """A folder in ink with the accent sparkle on its face: where the
    assistant's tools live on disk."""
    w = 12 if c.small else 7
    c.rrect(8, 30, 88, 82, rad=6, w=w)
    c.line([(8, 30), (12, 18), (40, 18), (48, 30)], w=w)
    sparkle(c, 62, 58, 16 if c.small else 14)


# --- Smoke fixture -----------------------------------------------------------
# Icons for the Smoke.extension test fixture. Same design language, but these
# glyphs describe the CONTRACT under test, not a user-facing tool. The alert
# red stays reserved for Purge; nothing here may use it.

def ic_smoke_toggle_off(c):
    """Hollow ring: the toggle at rest."""
    w = 15 if c.small else 9
    c.circle(48, 48, 27 if c.small else 29, w=w)


def ic_smoke_toggle_on(c):
    """The same ring filled with accent: the toggle engaged."""
    c.disc(48, 48, 33 if c.small else 34, c.t['accent'])
    c.disc(48, 48, 12, c.t['paper'])


def ic_smoke_smart(c):
    """A button that redraws itself: the accent diamond is the self-applied
    change, sitting on the button's corner."""
    if c.small:
        c.rrect(8, 22, 74, 88, rad=10, w=13)
        c.poly([(74, 8), (92, 26), (74, 44), (56, 26)],
               color=c.t['accent'], fill=c.t['accent'], w=0)
    else:
        c.rrect(12, 26, 70, 84, rad=8)
        c.poly([(72, 8), (90, 26), (72, 44), (54, 26)],
               color=c.t['accent'], fill=c.t['accent'], w=0)


def _split_glyph(c, accent_row):
    """Header bar over a two-item list; the accent row is the child the
    header reruns (0 = first, 1 = last)."""
    if c.small:
        c.rrect(8, 8, 88, 40, rad=8, w=13)
        rows = (62, 84)
        c.line([(10, rows[accent_row]), (86, rows[accent_row])],
               color=c.t['accent'], w=14)
        c.line([(10, rows[1 - accent_row]), (86, rows[1 - accent_row])], w=14)
    else:
        c.rrect(12, 10, 84, 36, rad=6)
        rows = (56, 74)
        c.line([(20, rows[accent_row]), (76, rows[accent_row])],
               color=c.t['accent'], w=9)
        c.line([(20, rows[1 - accent_row]), (76, rows[1 - accent_row])], w=8)


def ic_smoke_split_last(c):
    _split_glyph(c, accent_row=1)


def ic_smoke_split_fixed(c):
    _split_glyph(c, accent_row=0)


def _split_child(c, accent_row):
    """Two bars; the accent bar is this child's position in the menu."""
    rows = (34, 62)
    w = 16 if c.small else 10
    c.line([(14, rows[accent_row]), (82, rows[accent_row])],
           color=c.t['accent'], w=w)
    c.line([(14, rows[1 - accent_row]), (82, rows[1 - accent_row])], w=w)


def ic_smoke_child_first(c):
    _split_child(c, accent_row=0)


def ic_smoke_child_second(c):
    _split_child(c, accent_row=1)


def ic_smoke_url(c):
    """Globe with an accent arrow leaving it: opens outside the app."""
    if c.small:
        c.circle(42, 54, 32, w=13)
        c.line([(58, 38), (88, 8)], color=c.t['accent'], w=13)
        c.poly([(66, 6), (90, 6), (90, 30)],
               color=c.t['accent'], fill=c.t['accent'], w=0)
    else:
        c.circle(42, 54, 32)
        c.line([(10, 54), (74, 54)], w=6)
        c.d.ellipse([(42 - 14) * S, (54 - 32) * S, (42 + 14) * S, (54 + 32) * S],
                    outline=c.t['ink'], width=6 * S)
        c.line([(60, 36), (86, 10)], color=c.t['accent'], w=9)
        c.poly([(68, 6), (90, 6), (90, 28)],
               color=c.t['accent'], fill=c.t['accent'], w=0)


def ic_smoke_link(c):
    """Two chain links that do not meet: the target plugin is absent."""
    if c.small:
        c.circle(28, 28, 18, w=13)
        c.circle(68, 68, 18, w=13)
        c.line([(40, 56), (48, 48), (56, 56)], color=c.t['accent'], w=10)
    else:
        c.circle(28, 30, 17)
        c.circle(68, 66, 17)
        c.line([(40, 58), (46, 48), (42, 44)], color=c.t['accent'], w=7)
        c.line([(56, 38), (50, 48), (54, 52)], color=c.t['accent'], w=7)


def ic_smoke_lib(c):
    """A shelved book with the accent tick: the lib import resolved."""
    if c.small:
        c.rrect(10, 8, 56, 88, rad=6, w=13)
        c.line([(58, 60), (70, 76), (92, 40)], color=c.t['accent'], w=13)
    else:
        c.rrect(14, 10, 54, 86, rad=5)
        c.line([(14, 28), (54, 28)], w=6)
        c.line([(58, 60), (70, 74), (90, 42)], color=c.t['accent'], w=9)


def ic_smoke_selection(c):
    """Dashed marquee with the accent pointer: needs something selected."""
    dash_w = 12 if c.small else 7
    for a, b in (((16, 16), (36, 16)), ((48, 16), (68, 16)),
                 ((16, 16), (16, 36)), ((16, 48), (16, 68)),
                 ((68, 16), (68, 32)), ((16, 68), (32, 68))):
        c.line([a, b], w=dash_w)
    c.poly([(44, 44), (86, 60), (68, 68), (78, 86), (66, 92), (58, 72), (44, 84)],
           color=c.t['accent'], fill=c.t['accent'], w=0)


def ic_smoke_old(c):
    """Clock face: the version gate."""
    if c.small:
        c.circle(48, 48, 38, w=13)
        c.line([(48, 48), (48, 24)], color=c.t['accent'], w=12)
        c.line([(48, 48), (66, 58)], color=c.t['accent'], w=12)
    else:
        c.circle(48, 48, 36)
        c.line([(48, 48), (48, 22)], color=c.t['accent'], w=8)
        c.line([(48, 48), (68, 60)], color=c.t['accent'], w=8)


def ic_smoke_xaml(c):
    """Dialog window; the accent block is the button the XAML wires up."""
    if c.small:
        c.rrect(6, 12, 90, 84, rad=8, w=13)
        c.line([(6, 34), (90, 34)], w=13)
        c.rrect(50, 52, 78, 70, rad=4, w=0, fill=c.t['accent'])
    else:
        c.rrect(10, 14, 86, 82, rad=6)
        c.line([(10, 32), (86, 32)], w=6)
        c.line([(20, 48), (56, 48)], w=6)
        c.rrect(56, 58, 78, 72, rad=4, w=0, fill=c.t['accent'])


def ic_smoke_pane(c):
    """A docked panel against the edge in ink, with an accent content line:
    the live pane a dockpane bundle fills. This is the ribbon toggle's OFF art -
    the panel closed."""
    if c.small:
        c.rrect(4, 6, 60, 90, rad=6, w=12)
        c.line([(70, 20), (92, 20)], color=c.t['accent'], w=12)
        c.line([(70, 44), (92, 44)], color=c.t['accent'], w=12)
        c.line([(70, 68), (86, 68)], color=c.t['accent'], w=12)
    else:
        c.rrect(8, 10, 58, 86, rad=6)
        c.line([(68, 24), (90, 24)], color=c.t['accent'], w=7)
        c.line([(68, 46), (90, 46)], color=c.t['accent'], w=7)
        c.line([(68, 68), (84, 68)], color=c.t['accent'], w=7)


def ic_smoke_pane_on(c):
    """The same docked panel, filled solid with accent: the ribbon toggle's ON art,
    shown while the panel is open. Large only - a dockpane button is never shown
    at the small size, so there is no small on-art to draw."""
    c.rrect(8, 10, 58, 86, rad=6, w=0, fill=c.t['accent'])
    c.line([(68, 24), (90, 24)], color=c.t['ink'], w=7)
    c.line([(68, 46), (90, 46)], color=c.t['ink'], w=7)
    c.line([(68, 68), (84, 68)], color=c.t['ink'], w=7)


ICONS = {
    'pyACAD.panel/Console.pushbutton': ic_console,
    'pyACAD.panel/Reload.pushbutton': ic_reload,
    'pyACAD.panel/Shortcuts.pushbutton': ic_shortcuts,
    'pyACAD.panel/Settings.pushbutton': ic_settings,
    'AI_(beta).panel/Ask_AI.dockpane': ic_ask_ai,
    'AI_(beta).panel/Setup.stack/AI_Settings.pushbutton': ic_ai_settings,
    'AI_(beta).panel/Setup.stack/Open_AI_Folder.pushbutton': ic_open_ai_folder,
}

SMOKE_ICONS = {
    'Buttons.panel/02_Smart_Button.smartbutton': ic_smoke_smart,
    'Buttons.panel/03_Split_LastUsed.splitbutton': ic_smoke_split_last,
    'Buttons.panel/03_Split_LastUsed.splitbutton/01_Alpha.pushbutton': ic_smoke_child_first,
    'Buttons.panel/03_Split_LastUsed.splitbutton/02_Beta.pushbutton': ic_smoke_child_second,
    'Buttons.panel/04_Split_Fixed.splitpushbutton': ic_smoke_split_fixed,
    'Buttons.panel/04_Split_Fixed.splitpushbutton/01_First.pushbutton': ic_smoke_child_first,
    'Buttons.panel/04_Split_Fixed.splitpushbutton/02_Second.pushbutton': ic_smoke_child_second,
    'Buttons.panel/05_Url_Demo.urlbutton': ic_smoke_url,
    'Buttons.panel/06_Link_Missing.linkbutton': ic_smoke_link,
    'Buttons.panel/07_Needs_Selection.pushbutton': ic_smoke_selection,
    'Buttons.panel/09_Old_Only.pushbutton': ic_smoke_old,
    'Buttons.panel/10_Lib_Check.pushbutton': ic_smoke_lib,
    'Dialogs.panel/Xaml_Dialog.pushbutton': ic_smoke_xaml,
    'Panes.panel/01_Pane_Demo.dockpane': ic_smoke_pane,
}

# (tab root, bundle) -> (off drawing, on drawing). Toggles get icon.off/.on
# pairs instead of a plain icon.png; the small files render the off state.
TOGGLES = {
    (SMOKE_TAB, 'Buttons.panel/01_Toggle_Demo.toggle'):
        (ic_smoke_toggle_off, ic_smoke_toggle_on),
}

# (tab root, bundle) -> on drawing. A *.dockpane keeps its ordinary icon.png as the
# standard four (registered in ICONS/SMOKE_ICONS above, drawn OFF/closed) and gets
# ONE extra pair, icon.on(.dark).png, for its ribbon toggle's pressed state - large
# only, since a dockpane button is never shown at the small size.
DOCKPANE_ON = {
    (SMOKE_TAB, 'Panes.panel/01_Pane_Demo.dockpane'): ic_smoke_pane_on,
    (TAB, 'AI_(beta).panel/Ask_AI.dockpane'): ic_ask_ai_on,
}

def _load_panel_icons():
    """Merge the per-panel drawing modules in tools/icons into ICONS/DOCKPANE_ON."""
    import importlib
    import sys
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import icons as panel_icons
    for name in panel_icons.MODULES:
        try:
            mod = importlib.import_module('icons.' + name)
        except ImportError:
            continue
        for bundle, fn in getattr(mod, 'ICONS', {}).items():
            if bundle in ICONS:
                raise SystemExit('duplicate icon registration: ' + bundle)
            ICONS[bundle] = fn
        for bundle, fn in getattr(mod, 'DOCKPANE_ON', {}).items():
            DOCKPANE_ON[(TAB, bundle)] = fn


_load_panel_icons()

GROUPS = [(TAB, ICONS), (SMOKE_TAB, SMOKE_ICONS)]


def render(fn, theme, small):
    c = Ctx(theme, WEIGHT_SMALL if small else WEIGHT_LARGE)
    fn(c)
    return c


def write_all():
    missing = [b for root, icons in GROUPS for b in icons
               if not os.path.isdir(os.path.join(root, b.replace('/', os.sep)))]
    missing += [b for root, b in TOGGLES
                if not os.path.isdir(os.path.join(root, b.replace('/', os.sep)))]
    missing += [b for root, b in DOCKPANE_ON
                if not os.path.isdir(os.path.join(root, b.replace('/', os.sep)))]
    if missing:
        raise SystemExit('bundle folder(s) missing:\n  ' + '\n  '.join(sorted(missing)))

    written = 0
    bundles = 0
    for root, icons in GROUPS:
        for bundle, fn in sorted(icons.items()):
            folder = os.path.join(root, bundle.replace('/', os.sep))
            for suffix, theme in THEMES:
                render(fn, theme, False).png(LARGE_PX).save(
                    os.path.join(folder, 'icon%s.png' % suffix))
                render(fn, theme, True).png(SMALL_PX).save(
                    os.path.join(folder, 'icon.small%s.png' % suffix))
                written += 2
            bundles += 1
            print('  ' + bundle)
    for (root, bundle), (off_fn, on_fn) in sorted(TOGGLES.items()):
        folder = os.path.join(root, bundle.replace('/', os.sep))
        for suffix, theme in THEMES:
            render(off_fn, theme, False).png(LARGE_PX).save(
                os.path.join(folder, 'icon.off%s.png' % suffix))
            render(on_fn, theme, False).png(LARGE_PX).save(
                os.path.join(folder, 'icon.on%s.png' % suffix))
            render(off_fn, theme, True).png(SMALL_PX).save(
                os.path.join(folder, 'icon.small%s.png' % suffix))
            written += 3
        bundles += 1
        print('  ' + bundle + ' (toggle)')
    for (root, bundle), on_fn in sorted(DOCKPANE_ON.items()):
        folder = os.path.join(root, bundle.replace('/', os.sep))
        for suffix, theme in THEMES:
            render(on_fn, theme, False).png(LARGE_PX).save(
                os.path.join(folder, 'icon.on%s.png' % suffix))
            written += 1
        bundles += 1
        print('  ' + bundle + ' (dockpane on-art)')
    print('done: %d bundles, %d files' % (bundles, written))


def write_sheet(path):
    """Contact sheet for reviewing the whole set without opening AutoCAD."""
    entries = [(b, fn) for _, icons in GROUPS for b, fn in icons.items()]
    for (_, bundle), (off_fn, on_fn) in sorted(TOGGLES.items()):
        entries.append((bundle + '_off', off_fn))
        entries.append((bundle + '_on', on_fn))
    for (_, bundle), on_fn in sorted(DOCKPANE_ON.items()):
        entries.append((bundle + '_on', on_fn))
    img = Image.new('RGB', (560, 24 + len(entries) * 60), (243, 243, 243))
    d = ImageDraw.Draw(img)
    y = 14
    for bundle, fn in entries:
        label = bundle.rsplit('/', 1)[-1].split('.')[0]
        d.text((14, y + 20), label.lstrip('0123456789_')[:24], fill=(60, 60, 60))
        img.paste(Image.new('RGB', (170, 56), (31, 31, 31)), (330, y - 4))
        for x, theme in ((200, LIGHT), (350, DARK)):
            big = render(fn, theme, False).png(48)
            img.paste(big, (x, y), big)
            sm = render(fn, theme, True).png(16)
            img.paste(sm, (x + 62, y + 16), sm)
        y += 60
    img.save(path)
    print('sheet: ' + path)


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--sheet', help='also write a contact sheet here')
    ap.add_argument('--sheet-only', action='store_true',
                    help='write only the contact sheet, touch no bundle')
    args = ap.parse_args()

    if not args.sheet_only:
        write_all()
    if args.sheet:
        write_sheet(args.sheet)


if __name__ == '__main__':
    main()
