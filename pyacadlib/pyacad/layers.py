"""Layers: list, read, create, rename, delete, merge, freeze/thaw, isolate, snapshots.

Pattern arguments take '*' and '?' wildcards, case-insensitively, or a list of
patterns; matches() is the pure test for one name. Freeze-style functions
return how many layers changed and skip the current layer where AutoCAD would
refuse. The AutoCAD API is imported inside each function.
"""

import re as _re

_PROTECTED = ('0', 'defpoints')


def matches(name, pattern):
    """True when name fits pattern ('*' any run, '?' one character), any case."""
    regex = ''.join('.*' if c == '*' else '.' if c == '?' else _re.escape(c)
                    for c in str(pattern))
    return _re.match('^' + regex + '$', str(name), _re.IGNORECASE | _re.DOTALL) is not None


def matches_any(name, patterns):
    """True when name fits any of the patterns (one string or a list)."""
    if isinstance(patterns, str):
        patterns = [patterns]
    return any(matches(name, p) for p in patterns)


def names():
    """Every layer name, in table order."""
    from pyacad import doc
    with doc.transaction() as tr:
        return [name for name, _ in _records(tr)]


def current():
    """The current layer's name."""
    from pyacad import doc, ents
    with doc.transaction() as tr:
        return str(ents._open_id(tr, doc.database().Clayer).Name)


def set_current(name):
    """Makes the named layer current (it is thawed and turned on first)."""
    from pyacad import doc, ents
    with doc.transaction() as tr:
        oid = _id(tr, name)
        record = ents._open_id(tr, oid, write=True)
        record.IsFrozen = False
        record.IsOff = False
        doc.database().Clayer = oid


def exists(name):
    """True when the drawing has a layer of that name."""
    from pyacad import doc, ents
    with doc.transaction() as tr:
        return bool(ents._open_id(tr, doc.database().LayerTableId).Has(str(name)))


def get(name):
    """{name, color, linetype, lineweight, is_off, is_frozen, is_locked, is_plottable}."""
    from pyacad import doc, ents
    with doc.transaction() as tr:
        return _state(tr, ents._open_id(tr, _id(tr, name)))


def create(name, color=7, linetype='Continuous'):
    """Adds a layer; ValueError when it exists or the name or linetype is bad."""
    from pyacad import _api, doc, ents
    import System
    ds = _api.DatabaseServices
    name = str(name)
    if isinstance(color, bool) or not isinstance(color, int) or not 1 <= color <= 255:
        raise ValueError('color is a color index from 1 to 255, not %r.' % (color,))
    try:
        ds.SymbolUtilityServices.ValidateSymbolName(name, False)
    except Exception:
        raise ValueError('%r is not a valid layer name.' % name)
    db = doc.database()
    with doc.transaction() as tr:
        table = ents._open_id(tr, db.LayerTableId)
        if table.Has(name):
            raise ValueError('Layer %s already exists.' % name)
        linetypes = ents._open_id(tr, db.LinetypeTableId)
        if not linetypes.Has(str(linetype)):
            raise ValueError('No linetype is named %s.' % linetype)
        record = ds.LayerTableRecord()
        record.Name = name
        record.Color = _api.Colors.Color.FromColorIndex(_api.Colors.ColorMethod.ByAci,
                                                         System.Int16(color))
        record.LinetypeObjectId = linetypes[str(linetype)]
        table.UpgradeOpen()
        table.Add(record)
        tr.AddNewlyCreatedDBObject(record, True)
    return name


def rename(old, new):
    """Renames a layer; layer 0 and Defpoints cannot be renamed."""
    from pyacad import _api, doc, ents
    old, new = str(old), str(new)
    if old.lower() in _PROTECTED:
        raise ValueError('Layer %s cannot be renamed.' % old)
    try:
        _api.DatabaseServices.SymbolUtilityServices.ValidateSymbolName(new, False)
    except Exception:
        raise ValueError('%r is not a valid layer name.' % new)
    with doc.transaction() as tr:
        oid = _id(tr, old)
        table = ents._open_id(tr, doc.database().LayerTableId)
        if table.Has(new) and table[new] != oid:
            raise ValueError('Layer %s already exists.' % new)
        ents._open_id(tr, oid, write=True).Name = new


def delete(name):
    """Erases a layer; ValueError when it is current, 0, Defpoints,
    xref-dependent or still in use."""
    from pyacad import doc
    with doc.transaction() as tr:
        _erase(tr, str(name))


def merge(sources, target, progress=None, cancelled=None):
    """Moves every entity on the source layers to target, then deletes the
    sources. One transaction; returns how many entities moved."""
    from pyacad import _api, doc, ents
    target = str(target)
    sources = [s for s in doc.as_names(sources) if s.lower() != target.lower()]
    db = doc.database()
    moved = 0
    with doc.transaction() as tr:
        target_id = _id(tr, target)
        source_ids = [_id(tr, s) for s in sources]
        wanted = [s.lower() for s in sources]
        if db.Clayer in source_ids:
            db.Clayer = target_id
        entity_ids = []
        for block_id in ents._local_block_ids(tr, db):
            entity_ids.extend(ents._open_id(tr, block_id))
        write = _api.DatabaseServices.OpenMode.ForWrite
        for index, oid in enumerate(entity_ids):
            if doc.step(index, len(entity_ids), progress, cancelled):
                raise RuntimeError('Merge cancelled; nothing was changed.')
            entity = ents._open_id(tr, oid)
            owned = []
            if str(oid.ObjectClass.DxfName) == 'INSERT':
                owned = [a for a in entity.AttributeCollection if not a.IsErased]
            for position, item_id in enumerate([oid] + owned):
                item = entity if position == 0 else ents._open_id(tr, item_id)
                if str(item.Layer).lower() in wanted:
                    # forceOpenOnLockedLayer: a locked source layer still merges
                    tr.GetObject(item_id, write, False, True).LayerId = target_id
                    moved += 1
        for source in sources:
            _erase(tr, source)
    return moved


def freeze(pattern):
    """Freezes matching layers (never the current one)."""
    return _set_flag(pattern, 'IsFrozen', True)


def thaw(pattern):
    """Thaws matching layers."""
    return _set_flag(pattern, 'IsFrozen', False)


def turn_off(pattern):
    """Turns matching layers off."""
    return _set_flag(pattern, 'IsOff', True)


def turn_on(pattern):
    """Turns matching layers on."""
    return _set_flag(pattern, 'IsOff', False)


def lock(pattern):
    """Locks matching layers."""
    return _set_flag(pattern, 'IsLocked', True)


def unlock(pattern):
    """Unlocks matching layers."""
    return _set_flag(pattern, 'IsLocked', False)


def isolate(names):
    """Thaws and turns on the named layers and freezes every other one; the
    current layer moves to the first named layer. Returns how many froze."""
    from pyacad import doc, ents
    keep = doc.as_names(names)
    if not keep:
        raise ValueError('isolate needs at least one layer name.')
    db = doc.database()
    frozen = 0
    with doc.transaction() as tr:
        keep_ids = [_id(tr, k) for k in keep]
        for oid in keep_ids:
            record = ents._open_id(tr, oid, write=True)
            record.IsFrozen = False
            record.IsOff = False
        if db.Clayer not in keep_ids:
            db.Clayer = keep_ids[0]
        lowered = [k.lower() for k in keep]
        for name, oid in _records(tr):
            if name.lower() in lowered:
                continue
            record = ents._open_id(tr, oid)
            if not record.IsFrozen:
                record.UpgradeOpen()
                record.IsFrozen = True
                frozen += 1
    _regen()
    return frozen


def snapshot():
    """{name: get(name)} for every layer."""
    from pyacad import doc, ents
    with doc.transaction() as tr:
        return dict((name, _state(tr, ents._open_id(tr, oid))) for name, oid in _records(tr))


def restore(snapshot):
    """Puts back is_off, is_frozen and is_locked from a snapshot(); layers
    missing from the drawing are skipped. Returns how many layers changed."""
    from pyacad import doc, ents
    db = doc.database()
    changed = 0
    with doc.transaction() as tr:
        table = ents._open_id(tr, db.LayerTableId)
        for name, state in snapshot.items():
            if not table.Has(name):
                continue
            oid = table[name]
            record = ents._open_id(tr, oid)
            wanted = {
                'IsOff': bool(state.get('is_off', record.IsOff)),
                'IsFrozen': bool(state.get('is_frozen', record.IsFrozen)),
                'IsLocked': bool(state.get('is_locked', record.IsLocked)),
            }
            if oid == db.Clayer:
                wanted['IsFrozen'] = False
            differs = [k for k in sorted(wanted) if bool(getattr(record, k)) != wanted[k]]
            if differs:
                record.UpgradeOpen()
                for key in differs:
                    setattr(record, key, wanted[key])
                changed += 1
    _regen()
    return changed


def _records(tr):
    """(name, id) for every layer."""
    from pyacad import doc, ents
    result = []
    for oid in ents._open_id(tr, doc.database().LayerTableId):
        result.append((str(ents._open_id(tr, oid).Name), oid))
    return result


def _id(tr, name):
    from pyacad import doc, ents
    table = ents._open_id(tr, doc.database().LayerTableId)
    if not table.Has(str(name)):
        raise ValueError('No layer is named %s.' % name)
    return table[str(name)]


def _state(tr, record):
    from pyacad import ents
    return {
        'name': str(record.Name),
        'color': int(record.Color.ColorIndex),
        'linetype': str(ents._open_id(tr, record.LinetypeObjectId).Name),
        'lineweight': ents._int(record.LineWeight),
        'is_off': bool(record.IsOff),
        'is_frozen': bool(record.IsFrozen),
        'is_locked': bool(record.IsLocked),
        'is_plottable': bool(record.IsPlottable),
    }


def _erase(tr, name):
    from pyacad import _api, doc, ents
    if name.lower() in _PROTECTED:
        raise ValueError('Layer %s cannot be deleted.' % name)
    db = doc.database()
    oid = _id(tr, name)
    if oid == db.Clayer:
        raise ValueError('Layer %s is current and cannot be deleted.' % name)
    record = ents._open_id(tr, oid)
    if record.IsDependent:
        raise ValueError('Layer %s belongs to an xref and cannot be deleted.' % name)
    ids = _api.DatabaseServices.ObjectIdCollection()
    ids.Add(oid)
    db.Purge(ids)
    if ids.Count == 0:
        raise ValueError('Layer %s is in use and cannot be deleted.' % name)
    record.UpgradeOpen()
    record.Erase()


def _set_flag(pattern, flag, value):
    from pyacad import doc, ents
    db = doc.database()
    changed = 0
    with doc.transaction() as tr:
        for name, oid in _records(tr):
            if not matches_any(name, pattern):
                continue
            if flag == 'IsFrozen' and value and oid == db.Clayer:
                continue
            record = ents._open_id(tr, oid)
            if bool(getattr(record, flag)) != value:
                record.UpgradeOpen()
                setattr(record, flag, value)
                changed += 1
    if flag != 'IsLocked':
        _regen()
    return changed


def _regen():
    from pyacad import doc
    doc.editor().Regen()
