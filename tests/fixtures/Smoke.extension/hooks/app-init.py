"""Smoke fixture: fires once at boot and again after every Reload."""
from pyacad import toast

toast.info('Hook app-init ran')
