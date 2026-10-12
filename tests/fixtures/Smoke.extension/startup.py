"""Smoke fixture: proves startup.py runs at boot and on every Reload."""
from pyacad import toast

toast.info('Smoke.extension startup ran')
