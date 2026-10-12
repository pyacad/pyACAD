"""The active drawing: document, database, editor, transactions, system variables.

Every pyacad module reaches the drawing through this one: transaction() is the
single way in to the database. The AutoCAD API is imported inside each function,
so this module imports anywhere; calling an API function outside AutoCAD raises
ImportError. Everything here works in Core Console (no AcMgd needed).
"""

STEP = 200


def active():
    """The active Document; raises RuntimeError when no drawing is open."""
    from pyacad import _api
    document = _api.Application.DocumentManager.MdiActiveDocument
    if document is None:
        raise RuntimeError('No drawing is open.')
    return document


def database():
    """The active drawing's Database."""
    return active().Database


def editor():
    """The active drawing's Editor."""
    return active().Editor


def path():
    """The drawing's full path, or 'untitled' when it was never saved."""
    document = active()
    if not document.IsNamedDrawing:
        return 'untitled'
    return str(document.Database.Filename)


def is_saved():
    """True when the drawing has a file and no unsaved changes (DBMOD is 0)."""
    if not active().IsNamedDrawing:
        return False
    return int(sysvar('DBMOD')) == 0


def is_read_only():
    """True when the drawing was opened read-only."""
    return bool(active().IsReadOnly)


class _Transaction(object):
    def __enter__(self):
        self._tr = database().TransactionManager.StartTransaction()
        return self._tr

    def __exit__(self, kind, value, traceback):
        try:
            if kind is None:
                self._tr.Commit()
            else:
                self._tr.Abort()
        finally:
            self._tr.Dispose()
        return False


def transaction():
    """Context manager over one database transaction.

        with doc.transaction() as tr:
            obj = tr.GetObject(object_id, OpenMode.ForRead)

    Commits on normal exit, aborts when the block raises.
    """
    return _Transaction()


class _Lock(object):
    def __enter__(self):
        self._lock = active().LockDocument()
        return self._lock

    def __exit__(self, kind, value, traceback):
        self._lock.Dispose()
        return False


def lock():
    """Context manager that locks the document, for code outside command
    context (hooks, palette and dialog callbacks). Commands are already locked."""
    return _Lock()


def save():
    """Saves the drawing to its own path in the current DWG format."""
    from pyacad import _api
    document = active()
    if not document.IsNamedDrawing:
        raise RuntimeError('The drawing has never been saved; there is no path to save to.')
    if document.IsReadOnly:
        raise RuntimeError('The drawing is read-only.')
    db = document.Database
    with lock():
        db.SaveAs(db.Filename, True, _api.DatabaseServices.DwgVersion.Current,
                  db.SecurityParameters)
    return str(db.Filename)


def units():
    """The drawing's insertion units name, such as 'Millimeters'."""
    return str(database().Insunits.ToString())


def open_documents():
    """Paths of every open drawing (the document name for untitled ones)."""
    from pyacad import _api
    result = []
    for document in _api.Application.DocumentManager:
        if document.IsNamedDrawing:
            result.append(str(document.Database.Filename))
        else:
            result.append(str(document.Name))
    return result


def send_command(text):
    """Queues a command line on the drawing; it runs after the script returns."""
    active().SendStringToExecute(str(text) + '\n', True, False, True)


def sysvar(name):
    """A system variable's value."""
    from pyacad import _api
    return _api.Application.GetSystemVariable(str(name))


def set_sysvar(name, value):
    """Sets a system variable. Integers go in as 16-bit first (most integer
    variables are), then 32-bit; booleans as 1 or 0."""
    from pyacad import _api
    import System
    name = str(name)
    if isinstance(value, bool):
        value = int(value)
    if isinstance(value, int):
        if -32768 <= value <= 32767:
            try:
                _api.Application.SetSystemVariable(name, System.Int16(value))
                return
            except Exception:
                pass
        _api.Application.SetSystemVariable(name, System.Int32(value))
    elif isinstance(value, float):
        _api.Application.SetSystemVariable(name, System.Double(value))
    else:
        _api.Application.SetSystemVariable(name, value)


def as_point(value):
    """(x, y) or (x, y, z) as a 3-tuple of floats; ValueError otherwise."""
    try:
        items = [float(v) for v in value]
    except (TypeError, ValueError):
        raise ValueError('A point is (x, y) or (x, y, z), not %r.' % (value,))
    if len(items) == 2:
        items.append(0.0)
    if len(items) != 3:
        raise ValueError('A point is (x, y) or (x, y, z), not %r.' % (value,))
    return tuple(items)


def as_names(value):
    """One name or an iterable of names as a list of strings."""
    if value is None:
        return []
    if isinstance(value, str):
        return [value]
    return [str(v) for v in value]


def step(index, total, progress=None, cancelled=None):
    """Reports progress every STEP items; True when the caller should stop."""
    if index % STEP != 0:
        return False
    if progress is not None:
        progress(index, total)
    return bool(cancelled is not None and cancelled())


def _point(p):
    return (float(p.X), float(p.Y), float(p.Z))


def _point3d(value):
    from pyacad import _api
    x, y, z = as_point(value)
    return _api.Geometry.Point3d(x, y, z)
