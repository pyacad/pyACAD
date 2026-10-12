"""pyacad - the pyACAD runtime library for AutoCAD python scripts.

Deliberately light at import time: no AutoCAD API references live here, so the
package imports (and unit-tests) outside AutoCAD. API-bound helpers live in the
submodules, which import the API (pyacad._api) when first used.
"""

__version__ = "0.1.0"
