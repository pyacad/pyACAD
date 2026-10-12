"""Smoke fixture: fires on selection changes (debounced 250ms).

Rapid clicking should yield one toast per pause, never one per click.
While a pyACAD command is running this hook is skipped (see the log).
"""
from pyacad import toast

toast.info('Hook selection-changed ran')
