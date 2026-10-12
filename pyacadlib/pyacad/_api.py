"""Loads the AutoCAD .NET API for the other pyacad modules (internal).

Importing this module requires a live AutoCAD process: outside one the API
assemblies (acdbmgd, acmgd, accoremgd) cannot resolve and a clear ImportError is
raised instead of a raw CLR loader exception. acmgd is optional: Core Console
has no AcMgd, and every pyacad module works from acdbmgd and accoremgd alone.
"""

import clr

_REQUIRED = ('acdbmgd', 'accoremgd')
_OPTIONAL = ('acmgd',)

_NOT_IN_HOST = ('pyacad: could not load the AutoCAD .NET API (%s). '
                'This module only works inside an AutoCAD session running pyACAD.')

try:
    for _name in _REQUIRED:
        clr.AddReference(_name)
except Exception:
    raise ImportError(_NOT_IN_HOST % 'acdbmgd, acmgd, accoremgd')



def _already_loaded(name):
    """True when the host itself loaded the assembly (AutoCAD does, Core Console does not)."""
    import System
    for assembly in System.AppDomain.CurrentDomain.GetAssemblies():
        if assembly.GetName().Name.lower() == name.lower():
            return True
    return False


# AddReference by bare name would find AcMgd.dll beside accoreconsole.exe and load the
# UI assembly into the Core Console, which kills the process without a traceback. Only
# reference it where the host already has it.
for _name in _OPTIONAL:
    if _already_loaded(_name):
        try:
            clr.AddReference(_name)
        except Exception:
            pass

import Autodesk.AutoCAD.ApplicationServices as ApplicationServices
import Autodesk.AutoCAD.DatabaseServices as DatabaseServices
import Autodesk.AutoCAD.EditorInput as EditorInput
import Autodesk.AutoCAD.Geometry as Geometry
import Autodesk.AutoCAD.Colors as Colors
import Autodesk.AutoCAD.PlottingServices as PlottingServices
import Autodesk.AutoCAD.Runtime as Runtime
from Autodesk.AutoCAD.ApplicationServices.Core import Application


def add_reference(name):
    """Loads an additional AutoCAD assembly, with the same clear failure mode."""
    try:
        clr.AddReference(name)
    except Exception:
        raise ImportError(_NOT_IN_HOST % name)
