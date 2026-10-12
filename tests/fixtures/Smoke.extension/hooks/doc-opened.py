"""Smoke fixture: fires when a document opens."""
from pyacad import toast

toast.info('Hook doc-opened ran')
