"""Layers: list, read, create, rename, delete, merge, freeze/thaw, isolate, snapshots.

Pattern arguments take '*' and '?' wildcards, case-insensitively, or a list of
patterns; matches() is the pure test for one name. Freeze-style functions
return how many layers changed and skip the current layer where AutoCAD would
refuse. The AutoCAD API is imported inside each function.

Pure helpers for layer standards and pickers: parse_mapping() reads an
old,new[,color][,linetype] CSV, plan_mapping() turns it into rename / merge /
create steps for a drawing's layer names, describe_action() words one step,
filter_names() is a filter box, snapshot_choices() lists saved snapshot slots.
"""

import re as _re

_PROTECTED = ('0', 'defpoints')

# Characters AutoCAD refuses in a layer name (the vertical bar marks xref layers).
_BAD_NAME_CHARS = '<>/\\":;?*|,=`'

_ACI_NAMES = {'red': 1, 'yellow': 2, 'green': 3, 'cyan': 4, 'blue': 5,
              'magenta': 6, 'white': 7, 'black': 7}

# A first CSV row reads as a header when its first two cells, letters only and
# lowercased, are one of these.
_OLD_HEADS = ('old', 'oldlayer', 'oldname', 'from', 'source', 'sourcelayer',
              'existing', 'existinglayer', 'layer', 'current', 'currentlayer')
_NEW_HEADS = ('new', 'newlayer', 'newname', 'to', 'target', 'targetlayer',
              'standard', 'standardlayer')


def _pattern(pattern):
    """pattern ('*' any run, '?' one character) as a compiled whole-name regex."""
    regex = ''.join('.*' if c == '*' else '.' if c == '?' else _re.escape(c)
                    for c in str(pattern))
    return _re.compile('^' + regex + '$', _re.IGNORECASE | _re.DOTALL)


def matches(name, pattern):
    """True when name fits pattern ('*' any run, '?' one character), any case."""
    return _pattern(pattern).match(str(name)) is not None


def matches_any(name, patterns):
    """True when name fits any of the patterns (one string or a list)."""
    if isinstance(patterns, str):
        patterns = [patterns]
    return any(matches(name, p) for p in patterns)


def filter_names(names, text):
    """The names that fit a filter box's text, in their order, as a new list.

    Blank text keeps every name. Each comma-separated term is a wildcard
    pattern when it holds '*' or '?' (so 'A-*' means "starts with A-"), else a
    case-insensitive substring; a name stays when any term fits it.
    """
    terms = [t.strip() for t in str(text or '').split(',')]
    terms = [t for t in terms if t]
    if not terms:
        return list(names)
    tests = []
    for term in terms:
        if '*' in term or '?' in term:
            tests.append(_pattern(term).match)
        else:
            tests.append(lambda name, part=term.lower(): part in name.lower())
    return [name for name in names if any(test(name) for test in tests)]


def parse_mapping(csv_text):
    """The rows of a layer-standard CSV as (old, new, color, linetype) tuples.

    Columns are old,new[,color][,linetype]; any after the fourth (a notes
    column) are ignored. old may use '*' and '?' wildcards. color is a color
    number from 1 to 255 or red, yellow, green, cyan, blue, magenta or white;
    color and linetype read None when missing or blank. Cells split on commas,
    semicolons or tabs, whichever the first row uses, and may be "quoted" with
    "" standing for a quote. Blank lines, a header row (old,new or from,to and
    the like) and a byte order mark are skipped. A row without an old or a new
    name, or with a color that is not one, raises ValueError naming its line.
    """
    text = '' if csv_text is None else csv_text
    if text[:1] == u'\ufeff':
        text = text[1:]
    delimiter = None
    first = True
    rows = []
    for number, line in enumerate(text.splitlines(), 1):
        if not line.strip():
            continue
        if delimiter is None:
            delimiter = _guess_delimiter(line)
        cells = _split_cells(line, delimiter)
        cells += [''] * (4 - len(cells))
        if not any(cells):
            continue
        if first:
            first = False
            if _is_header(cells):
                continue
        old, new, color, linetype = cells[:4]
        if not old:
            raise ValueError('Line %d has no old layer name.' % number)
        if not new:
            raise ValueError('Line %d: %s has no new layer name.' % (number, old))
        rows.append((old, new, _parse_color(color, number), linetype or None))
    return rows


def plan_mapping(existing_names, mapping):
    """What applying a parse_mapping() standard to a drawing does, step by step.

    Each existing layer, in the order given, meets the first row whose old
    pattern fits it and becomes ('rename', old, new) when no layer is called new
    yet, or ('merge', old, target) when one is (target in the drawing's own
    spelling), counting what the earlier steps renamed and merged away. Layer 0,
    Defpoints, xref layers and an invalid new name give ('skip', old, reason);
    a layer already named new gives nothing. Then every new name still missing
    from the drawing becomes ('create', new, color, linetype) once, taking the
    first color and linetype its rows give (None when none do).
    """
    present = dict((name.lower(), name) for name in existing_names)
    compiled = [(_pattern(row[0]), row) for row in mapping]
    reported = set()
    plan = []
    for name in existing_names:
        row = next((r for p, r in compiled if p.match(name)), None)
        if row is None or row[1] == name:
            continue
        new = row[1]
        if name.lower() in _PROTECTED:
            plan.append(('skip', name, 'layer 0 and Defpoints cannot be renamed or merged'))
        elif '|' in name:
            plan.append(('skip', name, 'belongs to an xref, so it cannot be renamed'))
        elif not _is_valid_name(new):
            plan.append(('skip', name, '%s is not a valid layer name' % new))
            reported.add(new.lower())
        elif new.lower() == name.lower() or new.lower() not in present:
            plan.append(('rename', name, new))
            present.pop(name.lower(), None)
            present[new.lower()] = new
        else:
            plan.append(('merge', name, present[new.lower()]))
            present.pop(name.lower(), None)

    missing = []
    wanted = {}
    for _, new, color, linetype in mapping:
        key = new.lower()
        if key in present:
            continue
        if not _is_valid_name(new):
            if key not in reported:
                plan.append(('skip', new, '%s is not a valid layer name' % new))
                reported.add(key)
            continue
        if key not in wanted:
            missing.append(key)
            wanted[key] = [new, color, linetype]
        else:
            if wanted[key][1] is None:
                wanted[key][1] = color
            if not wanted[key][2]:
                wanted[key][2] = linetype
    plan.extend(('create',) + tuple(wanted[key]) for key in missing)
    return plan


def describe_action(action):
    """One plan_mapping() step in words: 'Rename A to B', 'Merge A into B',
    'Create B (color 3, DASHED)', 'Skip A: reason'."""
    kind = action[0]
    if kind == 'rename':
        return 'Rename %s to %s' % (action[1], action[2])
    if kind == 'merge':
        return 'Merge %s into %s' % (action[1], action[2])
    if kind == 'create':
        extras = []
        if action[2] is not None:
            extras.append('color %d' % action[2])
        if action[3]:
            extras.append(str(action[3]))
        return 'Create %s' % action[1] + (' (%s)' % ', '.join(extras) if extras else '')
    if kind == 'skip':
        return 'Skip %s: %s' % (action[1], action[2])
    return ' '.join(str(part) for part in action)


def snapshot_choices(slots):
    """(label, name) per saved snapshot slot, newest first, then by name.

    slots maps a slot name to {'saved': 'YYYY-MM-DD HH:MM', 'layers':
    snapshot()}; the label reads 'Name  (saved 2026-10-12 14:05, 3 layers)'.
    Slots without a time, or that are not a dict at all, come last.
    """
    entries = []
    for name, slot in (slots or {}).items():
        slot = slot if isinstance(slot, dict) else {}
        saved = str(slot.get('saved') or '')
        count = len(slot.get('layers') or {})
        label = '%s  (%s%d layer%s)' % (name, 'saved %s, ' % saved if saved else '',
                                         count, '' if count == 1 else 's')
        entries.append((saved, name, label))
    entries.sort(key=lambda entry: entry[1].lower())
    entries.sort(key=lambda entry: entry[0], reverse=True)
    return [(label, name) for _, name, label in entries]


def _is_valid_name(name):
    return 0 < len(name) <= 255 and not any(c in _BAD_NAME_CHARS for c in name)


def _guess_delimiter(line):
    """Comma, semicolon or tab: whichever the line uses most outside quotes."""
    counts = {}
    quoted = False
    for ch in line:
        if ch == '"':
            quoted = not quoted
        elif not quoted and ch in ',;\t':
            counts[ch] = counts.get(ch, 0) + 1
    return max(',;\t', key=lambda d: counts.get(d, 0))


def _split_cells(line, delimiter):
    """One CSV line as stripped cells; quotes group, "" inside them is a quote."""
    cells, cell, quoted, i = [], [], False, 0
    while i < len(line):
        ch = line[i]
        if quoted:
            if ch == '"' and line[i + 1:i + 2] == '"':
                cell.append('"')
                i += 1
            elif ch == '"':
                quoted = False
            else:
                cell.append(ch)
        elif ch == '"':
            quoted = True
        elif ch == delimiter:
            cells.append(''.join(cell))
            cell = []
        else:
            cell.append(ch)
        i += 1
    cells.append(''.join(cell))
    return [c.strip() for c in cells]


def _is_header(cells):
    def letters(cell):
        return _re.sub('[^a-z]', '', cell.lower())
    return letters(cells[0]) in _OLD_HEADS and letters(cells[1]) in _NEW_HEADS


def _parse_color(cell, number):
    if not cell:
        return None
    key = cell.lower()
    if key in _ACI_NAMES:
        return _ACI_NAMES[key]
    try:
        value = float(key)
        whole = int(value)
    except (ValueError, OverflowError):
        whole = value = None
    if whole is None or whole != value or not 1 <= whole <= 255:
        raise ValueError('Line %d: color %s is not a color number from 1 to 255.'
                         % (number, cell))
    return whole


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
