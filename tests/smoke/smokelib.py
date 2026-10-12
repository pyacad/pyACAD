# -*- coding: utf-8 -*-
"""Shared helper for the automated smoke probes (tests/smoke/probes/*.py).

Each probe runs inside AutoCAD through PYACAD, records named checks and writes
<run>/results/<probe>.json for tools/smoke/run-smoke.ps1 to read. The run folder
comes from the PYACAD_SMOKE_RUN environment variable the orchestrator sets before
it launches AutoCAD; the probe's own arguments arrive in __args__.
"""
import os
import json
import datetime
import traceback

import clr

clr.AddReference('PyAcad.Runtime')
clr.AddReference('AdWindows')
clr.AddReference('PresentationFramework')
clr.AddReference('WindowsBase')
clr.AddReference('PresentationCore')


def run_dir():
    run = os.environ.get('PYACAD_SMOKE_RUN')
    if not run:
        raise RuntimeError('PYACAD_SMOKE_RUN is not set')
    return run


def mark(name, detail=''):
    folder = os.path.join(run_dir(), 'markers')
    if not os.path.isdir(folder):
        os.makedirs(folder)
    with open(os.path.join(folder, name + '.log'), 'a') as f:
        f.write(datetime.datetime.now().strftime('%H:%M:%S.%f') + '\t' + detail + '\n')


class Probe(object):
    """Collects checks; `finish()` always writes the JSON, even after a crash."""

    def __init__(self, name, script_globals):
        self.name = name
        self.checks = []
        self.g = script_globals
        args = [str(a) for a in (script_globals.get('__args__') or [])]
        # The orchestrator appends the results-file name as the last argument.
        if args and args[-1].startswith('@'):
            self.name = args[-1][1:]
            args = args[:-1]
        self.args = args

    def check(self, name, ok, detail=''):
        self.checks.append({'name': name, 'ok': bool(ok), 'detail': str(detail)})
        return ok

    def equal(self, name, actual, expected):
        return self.check(name, actual == expected, 'actual=%r expected=%r' % (actual, expected))

    def finish(self):
        folder = os.path.join(run_dir(), 'results')
        if not os.path.isdir(folder):
            os.makedirs(folder)
        payload = {
            'probe': self.name,
            'ok': all(c['ok'] for c in self.checks) and bool(self.checks),
            'checks': self.checks,
            'written': datetime.datetime.now().isoformat(),
        }
        with open(os.path.join(folder, self.name + '.json'), 'w') as f:
            json.dump(payload, f, indent=2)

    def run(self, body):
        try:
            body(self)
        except Exception:
            self.check('no exception', False, traceback.format_exc())
        finally:
            self.finish()


# ---- ribbon introspection (AdWindows) ------------------------------------------

def ribbon():
    from Autodesk.Windows import ComponentManager
    return ComponentManager.Ribbon


def tab_titles():
    return [t.Title for t in ribbon().Tabs]


def find_tab(title):
    for t in ribbon().Tabs:
        if t.Title == title:
            return t
    return None


def walk_items(container):
    """Every RibbonItem under a tab, panel or item, depth first."""
    items = getattr(container, 'Items', None)
    if items is None:
        return
    for item in items:
        yield item
        for nested in walk_items(item):
            yield nested


def tab_items(title):
    tab = find_tab(title)
    if tab is None:
        return []
    out = []
    for panel in tab.Panels:
        out.extend(walk_items(panel.Source))
    return out


def _plain(text):
    """Ribbon captions carry a trailing marker for a config.py twin; compare without it."""
    return (text or '').replace(u'●', '').strip()


def find_button(tab_title, text):
    for item in tab_items(tab_title):
        if _plain(getattr(item, 'Text', None)) == text:
            return item
    return None


def button_texts(tab_title):
    return [_plain(i.Text) for i in tab_items(tab_title) if getattr(i, 'Text', None)]


# ---- runtime introspection ---------------------------------------------------------

def registered_commands():
    from PyAcad.Runtime.Execution import CommandRegistry
    return list(CommandRegistry.RegisteredNames)


def extension_titles():
    from PyAcad.Runtime import RuntimeHost
    return [os.path.basename(e.Directory) for e in RuntimeHost.Extensions]


def pane_handle(bundle_key):
    from PyAcad.Runtime.Panes import PaletteRegistry
    return PaletteRegistry.Find(bundle_key)


def marker_lines(name):
    path = os.path.join(run_dir(), 'markers', name + '.log')
    if not os.path.exists(path):
        return []
    with open(path) as f:
        return [l.rstrip('\n') for l in f if l.strip()]


def wpf_windows():
    """Every live WPF Window in the process. AutoCAD hosts no System.Windows.Application,
    so Application.Current is None; PresentationSource knows every HwndSource instead."""
    from System.Windows import PresentationSource, Window
    out = []
    for source in PresentationSource.CurrentSources:
        root = getattr(source, 'RootVisual', None)
        if isinstance(root, Window):
            out.append(root)
    return out


def main_window_rect():
    """AutoCAD's main window as (left, top, width, height) in screen pixels."""
    import ctypes
    from ctypes import wintypes
    from PyAcad.Runtime.Host import HostInfo
    rect = wintypes.RECT()
    ctypes.windll.user32.GetWindowRect(wintypes.HWND(int(HostInfo.MainWindowHandle.ToInt64())), ctypes.byref(rect))
    return (rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top)
