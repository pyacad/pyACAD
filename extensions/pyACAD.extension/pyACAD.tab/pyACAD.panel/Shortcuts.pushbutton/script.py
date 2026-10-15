"""View and change pyACAD keyboard shortcuts and aliases (writes config.json overrides)."""
import clr

clr.AddReference('PyAcad.Runtime')
from PyAcad.Runtime.Forms import ShortcutsDialog

from pyacad import toast

if ShortcutsDialog.ShowAndSave():
    __pyacad__.Reload()
    toast.success('Shortcuts and aliases saved and applied.')
