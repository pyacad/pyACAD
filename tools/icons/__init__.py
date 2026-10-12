"""Per-panel icon drawings for tools/make_icons.py.

Each module here (data.py, layers.py, xrefs.py, cleanup.py, output.py, takeoff.py)
exports an ICONS dict mapping 'Panel.panel/Bundle.pushbutton' to a drawing
function taking a make_icons.Ctx, and optionally a DOCKPANE_ON dict mapping a
dockpane bundle path to its on-state drawing. make_icons merges them all.
"""
MODULES = ['data', 'layers', 'xrefs', 'cleanup', 'output', 'takeoff']
