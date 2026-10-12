"""View and rebind pyACAD keyboard shortcuts (writes config.json overrides)."""
import clr

clr.AddReference('PyAcad.Runtime')
from PyAcad.Runtime.Forms import ShortcutsDialog

from pyacad import toast

if ShortcutsDialog.ShowAndSave():
    __pyacad__.Reload()
    toast.success('Shortcuts saved and applied.')
