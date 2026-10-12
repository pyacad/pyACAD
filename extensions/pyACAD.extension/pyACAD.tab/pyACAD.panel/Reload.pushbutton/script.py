"""Rescan extension folders and rebuild the pyACAD ribbon (no restart needed)."""
from pyacad import toast

__pyacad__.Reload()
toast.success('pyACAD ribbon reloaded.')
