"""External references: list, reload, unload, detach, bind and repath.

Path helpers (apply_replace, make_relative, make_absolute, status_name) are
pure and use Windows path rules on every platform. Reload, unload, detach and
bind run outside any open transaction, as AutoCAD requires; call them from a
command or inside pyacad.doc.lock(). The AutoCAD API is imported inside each
function.
"""

import os as _os  # loads ntpath first; importing ntpath alone is circular under IronPython
import ntpath as _ntpath
import re as _re

_STATUS = {
    'Resolved': 'loaded',
    'Unloaded': 'unloaded',
    'FileNotFound': 'not-found',
    'Unresolved': 'unresolved',
    'Unreferenced': 'unreferenced',
}


def status_name(xref_status):
    """The list() status for an XrefStatus enum name ('Resolved' is 'loaded')."""
    return _STATUS.get(str(xref_status), str(xref_status).lower())


def apply_replace(path, find, replace):
    """path with every occurrence of find replaced, ignoring case."""
    if not find:
        raise ValueError('find must not be empty.')
    return _re.sub(_re.escape(str(find)), lambda m: str(replace), str(path),
                   flags=_re.IGNORECASE)


def make_relative(path, host_dir):
    """path relative to host_dir ('.\\x.dwg', '..\\a\\x.dwg'). Relative paths,
    and paths on another drive or share, come back unchanged."""
    path = str(path)
    if not _ntpath.isabs(path) or not host_dir:
        return path
    if _ntpath.splitdrive(path)[0].lower() != _ntpath.splitdrive(str(host_dir))[0].lower():
        return path
    try:
        relative = _ntpath.relpath(path, str(host_dir))
    except ValueError:
        return path
    if relative.startswith('..'):
        return relative
    return '.\\' + relative


def make_absolute(path, host_dir):
    """A relative path resolved against host_dir; absolute paths come back
    normalized, unchanged otherwise."""
    path = str(path)
    if _ntpath.isabs(path):
        return _ntpath.normpath(path)
    if not host_dir:
        return path
    return _ntpath.normpath(_ntpath.join(str(host_dir), path))


def list():
    """[{name, path, found_at, status, is_overlay, nested}] for every xref."""
    from pyacad import doc, ents
    db = doc.database()
    result = []
    graph = db.GetHostDwgXrefGraph(True)
    with doc.transaction() as tr:
        for index in range(1, graph.NumNodes):
            node = graph.GetXrefNode(index)
            record = ents._open_id(tr, node.BlockTableRecordId)
            found = node.Database.Filename if node.Database is not None else ''
            result.append({
                'name': str(node.Name),
                'path': str(record.PathName),
                'found_at': str(found or ''),
                'status': status_name(node.XrefStatus.ToString()),
                'is_overlay': bool(record.IsFromOverlayReference),
                'nested': bool(node.IsNested),
            })
    return result


def reload(names=None):
    """Reloads the named xrefs (all when None); returns how many."""
    from pyacad import doc
    ids = _ids(names)
    if ids.Count:
        doc.database().ReloadXrefs(ids)
    return ids.Count


def unload(names):
    """Unloads the named xrefs; returns how many."""
    from pyacad import doc
    ids = _ids(names)
    if ids.Count:
        doc.database().UnloadXrefs(ids)
    return ids.Count


def detach(name):
    """Detaches one xref."""
    from pyacad import doc
    ids = _ids(name)
    doc.database().DetachXref(ids[0])


def bind(names, insert=False):
    """Binds the named xrefs into the drawing (insert=True binds like INSERT,
    without the xref name prefix); returns how many."""
    from pyacad import doc
    ids = _ids(names)
    if ids.Count:
        doc.database().BindXrefs(ids, bool(insert))
    return ids.Count


def repath(name, new_path):
    """Points one xref at a new path and reloads it."""
    _repath({str(name): str(new_path)})


def find_replace(find, replace):
    """Replaces text in every xref path (ignoring case) and reloads the
    changed ones; returns how many were repathed."""
    changes = {}
    for xref in list():
        if xref['nested']:
            continue
        updated = apply_replace(xref['path'], find, replace)
        if updated != xref['path']:
            changes[xref['name']] = updated
    _repath(changes)
    return len(changes)


def to_relative():
    """Makes every top-level xref path relative to the drawing; returns how
    many changed. The drawing must have been saved."""
    return _rebase(make_relative)


def to_absolute():
    """Makes every top-level xref path absolute; returns how many changed."""
    return _rebase(make_absolute)


def _rebase(convert):
    from pyacad import doc
    if doc.path() == 'untitled':
        raise RuntimeError('Save the drawing first; paths are relative to its folder.')
    host_dir = _ntpath.dirname(doc.path())
    changes = {}
    for xref in list():
        if xref['nested']:
            continue
        updated = convert(xref['path'], host_dir)
        if updated != xref['path']:
            changes[xref['name']] = updated
    _repath(changes)
    return len(changes)


def _repath(changes):
    from pyacad import _api, doc, ents
    if not changes:
        return
    ids = _api.DatabaseServices.ObjectIdCollection()
    with doc.transaction() as tr:
        for name in sorted(changes):
            oid = _xref_id(tr, name)
            ents._open_id(tr, oid, write=True).PathName = changes[name]
            ids.Add(oid)
    doc.database().ReloadXrefs(ids)


def _ids(names):
    """ObjectIdCollection of the named xrefs; every xref when names is None."""
    from pyacad import _api, doc
    if names is None:
        wanted = [x['name'] for x in list() if not x['nested']]
    else:
        wanted = doc.as_names(names)
    ids = _api.DatabaseServices.ObjectIdCollection()
    with doc.transaction() as tr:
        for name in wanted:
            ids.Add(_xref_id(tr, name))
    return ids


def _xref_id(tr, name):
    from pyacad import doc, ents
    table = ents._open_id(tr, doc.database().BlockTableId)
    if not table.Has(str(name)):
        raise ValueError('No xref is named %s.' % name)
    oid = table[str(name)]
    if not ents._open_id(tr, oid).IsFromExternalReference:
        raise ValueError('%s is a block, not an xref.' % name)
    return oid
