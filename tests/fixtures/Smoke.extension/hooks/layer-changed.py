"""Smoke fixture: fires when a layer is added, renamed, changed or removed."""
from pyacad import toast

toast.info('Hook layer-changed ran')
