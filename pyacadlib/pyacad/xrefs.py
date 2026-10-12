"""External references: list, reload, unload, detach, bind and repath.

Path helpers (apply_replace, make_relative, make_absolute, status_name) are
pure and use Windows path rules on every platform, and so are the helpers the
Xrefs panel is built on: describe and status_label (picker labels),
plan_repath and format_plan (a repath worked out and previewed before
anything changes), headline, summarize and check_loaded (what an action did).
Reload, unload, detach and bind run outside any open transaction, as AutoCAD
requires; call them from a command or inside pyacad.doc.lock(). The AutoCAD
API is imported inside each function.
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


# --- Pure helpers for the Xrefs panel ----------------------------------------
# list() shadows the builtin inside this module, so nothing below calls list().

_MODES = ('relative', 'absolute', 'replace', 'folder')
_NESTED = 'nested, follows its parent'


def status_label(status):
    """A list() status as people read it: 'not-found' is 'not found', and a
    missing one is 'unknown'."""
    if not status:
        return 'unknown'
    return str(status).replace('-', ' ')


def describe(xref_rows):
    """One picker label per list() row, in order:
    'Site  [loaded]  ..\\Xref\\site.dwg'. Overlays and nested xrefs say so
    inside the brackets; a row with no saved path shows '(no path)'."""
    labels = []
    for row in xref_rows:
        flags = [status_label(row.get('status'))]
        if row.get('is_overlay'):
            flags.append('overlay')
        if row.get('nested'):
            flags.append('nested')
        labels.append('%s  [%s]  %s' % (row.get('name', ''), ', '.join(flags),
                                        row.get('path') or '(no path)'))
    return labels


def plan_repath(xref_rows, mode, find=None, replace=None, folder=None, exists=None,
                host_dir=None):
    """What a repath would do: one (old_path, new_path, note) per row, in order.

    mode is 'relative' or 'absolute' (both need host_dir, the drawing's
    folder), 'replace' (find, ignoring case, becomes replace) or 'folder'
    (each xref points at <folder>\\<its file name>, but only where
    exists(that path) is true; exists defaults to os.path.isfile, so pass one
    to stay off the disk). note is '' for a row that changes; otherwise it
    says why not, and new_path is old_path. Nested xrefs never change: they
    follow the xref that holds them.
    """
    mode = str(mode or '').lower()
    if mode not in _MODES:
        raise ValueError('mode is one of %s, not %r.' % (', '.join(_MODES), mode))
    if mode in ('relative', 'absolute') and not host_dir:
        raise ValueError('Save the drawing first; paths are relative to its folder.')
    if mode == 'replace' and not find:
        raise ValueError('find must not be empty.')
    if mode == 'folder':
        if not folder:
            raise ValueError('folder must not be empty.')
        if exists is None:
            exists = _os.path.isfile
    plan = []
    for row in xref_rows:
        old = str(row.get('path') or '')
        if row.get('nested'):
            plan.append((old, old, _NESTED))
        elif not old:
            plan.append((old, old, 'no path'))
        else:
            new, note = _plan_one(old, mode, find, replace, folder, exists, host_dir)
            plan.append((old, old if note else new, note))
    return plan


def _plan_one(old, mode, find, replace, folder, exists, host_dir):
    """(new_path, note) for one top-level xref path; pure but for exists."""
    if mode == 'relative':
        if not _ntpath.isabs(old):
            return old, 'already relative'
        new = make_relative(old, host_dir)
        return (old, 'on another drive or share') if new == old else (new, '')
    if mode == 'absolute':
        new = make_absolute(old, host_dir)
        return (old, 'already absolute') if new == old else (new, '')
    if mode == 'replace':
        new = apply_replace(old, find, '' if replace is None else replace)
        return (old, 'no match') if new == old else (new, '')
    new = _ntpath.join(str(folder), _ntpath.basename(old))
    if _ntpath.normcase(new) == _ntpath.normcase(old):
        return old, 'already points there'
    if not exists(new):
        return old, 'not found in the folder'
    return new, ''


def format_plan(xref_rows, plan, limit=None):
    """plan_repath()'s result as preview text: each change as its name with
    from and to lines, then every row left alone with its note. limit caps
    each of the two lists; the rest become '...and N more'."""
    changes, kept = [], []
    for row, (old, new, note) in zip(xref_rows, plan):
        name = str(row.get('name', ''))
        if note:
            kept.append('  %s: %s' % (name, note))
        else:
            changes.append('%s\n  from  %s\n  to    %s' % (name, old, new))
    if changes:
        lines = ['Changes to %s:' % _count(len(changes), 'xref path'), '']
        for block in _cut(changes, limit, ''):
            lines += [block, '']
    else:
        lines = ['No xref path changes.', '']
    if kept:
        lines += ['Left as they are:'] + _cut(kept, limit, '  ')
    return '\n'.join(lines).rstrip('\n')


def headline(results, verb='Updated'):
    """One line for a toast: 'Reloaded 3 xrefs', or 'Reloaded 2 of 3 xrefs'
    when some failed. results are (name, error) pairs; a falsy error is a
    success."""
    results = [result for result in results]
    if not results:
        return 'No xrefs to update'
    done = len([name for name, error in results if not error])
    if done == len(results):
        return '%s %s' % (verb, _count(done, 'xref'))
    return '%s %d of %s' % (verb, done, _count(len(results), 'xref'))


def summarize(results, verb='Updated', limit=None):
    """The headline as a sentence, then every failure as 'name: error' under
    'Not done:'; limit caps the failure list. Text for an alert."""
    results = [result for result in results]
    text = headline(results, verb) + '.'
    failed = ['  %s: %s' % (name, error) for name, error in results if error]
    if failed:
        text += '\n\nNot done:\n' + '\n'.join(_cut(failed, limit, '  '))
    return text


def check_loaded(results, xref_rows):
    """results with each success whose xref did not end up loaded turned into
    a failure such as 'not found at ..\\b.dwg': AutoCAD takes a reload or a
    repath it cannot resolve without complaint. Pass the list() rows read
    after the action; names no longer among them (detached, bound) and
    earlier errors stay as they were."""
    rows = dict((str(row.get('name')), row) for row in xref_rows)
    checked = []
    for name, error in results:
        row = rows.get(str(name))
        if not error and row is not None and row.get('status') != 'loaded':
            label = status_label(row.get('status'))
            error = '%s at %s' % (label, row['path']) if row.get('path') else label
        checked.append((name, error))
    return checked


def _count(number, noun):
    return '%d %s' % (number, noun if number == 1 else noun + 's')


def _cut(lines, limit, indent):
    """lines, or the first limit of them and an '...and N more' line."""
    if limit is None or len(lines) <= limit:
        return lines
    return lines[:limit] + ['%s...and %d more' % (indent, len(lines) - limit)]

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


def repath(name, new_path, reload=True):
    """Points one xref at a new path and reloads it. reload=False only sets
    the path, which keeps an unloaded xref unloaded until its next reload."""
    _repath({str(name): str(new_path)}, reload_after=reload)


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


def _repath(changes, reload_after=True):
    from pyacad import _api, doc, ents
    if not changes:
        return
    ids = _api.DatabaseServices.ObjectIdCollection()
    with doc.transaction() as tr:
        for name in sorted(changes):
            oid = _xref_id(tr, name)
            ents._open_id(tr, oid, write=True).PathName = changes[name]
            ids.Add(oid)
    if reload_after:
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
