"""Version gate demo: on AutoCAD 2025+ clicking must toast the version
warning and abort; this line must never run there."""
from pyacad import toast

toast.error('Version gate FAILED: this script ran on an excluded host')
