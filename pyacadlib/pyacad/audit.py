"""Drawing health: purge, audit, zero-length and duplicate geometry, proxies,
and text styles whose font files cannot be found.

The pure half plans and reports: geometry_key(), the Drawing Cleanup steps
(STEPS, cleanup_plan(), format_report(), summary()) and the font and proxy
report (missing_fonts(), remap_plan(), proxy_summary(), format_font_report(),
font_report_rows()). Scans cover model space and every layout; proxies() also
looks inside block definitions. The AutoCAD API is imported inside each
function.
"""

_GEOMETRY = {
    'LINE': ('start', 'end'),
    'CIRCLE': ('center', 'radius'),
    'ARC': ('center', 'radius', 'start_angle', 'end_angle'),
    'LWPOLYLINE': ('points', 'closed'),
    'TEXT': ('text', 'height', 'position'),
    'MTEXT': ('text', 'height', 'position'),
    'INSERT': ('name', 'position', 'rotation', 'scale', 'attributes'),
}


def geometry_key(snapshot, tolerance=1e-6):
    """A hashable key for a pyacad.ents.get() snapshot: type, layer and
    geometry, with numbers snapped to tolerance. A LINE and its reverse share a
    key; an INSERT's attribute values are part of its key, so two tags stacked
    on one point with different values are not duplicates. None for types
    without a geometry rule."""
    type_name = str(snapshot.get('type', '')).upper()
    fields = _GEOMETRY.get(type_name)
    if fields is None:
        return None
    if tolerance <= 0:
        raise ValueError('tolerance must be above zero.')
    values = [_snap(snapshot.get(field), tolerance) for field in fields]
    if type_name == 'LINE':
        values = sorted(values)
    return (type_name, str(snapshot.get('layer', '')).lower()) + tuple(values)


# --- Drawing Cleanup: the steps, the order they run in, and the report -------

STEPS = (
    ('purge', 'Purge everything unused (repeats until nothing is left)', True),
    ('audit', 'Audit the drawing and fix errors', True),
    ('zero_length', 'Delete zero-length lines and polylines', True),
    ('duplicates', 'Delete duplicate entities (same type, layer and geometry)', True),
    ('bylayer', 'Set every entity colour to ByLayer', False),
    ('zoom', 'Zoom extents', True),
)
# (key, checklist label, ticked by default), in the order the steps run.

_DELETING = ('purge', 'zero_length', 'duplicates')

# key -> (report name, one done, many done, nothing done or queued)
_REPORT = {
    'purge': ('Purge', 'unused item removed', 'unused items removed', 'nothing unused'),
    'audit': ('Audit', None, None,
              'queued; it runs when this tool finishes and reports on the command line'),
    'zero_length': ('Zero-length', 'entity deleted', 'entities deleted', 'none found'),
    'duplicates': ('Duplicates', 'extra copy deleted', 'extra copies deleted', 'none found'),
    'bylayer': ('ByLayer colour', 'entity changed', 'entities changed', 'already ByLayer'),
    'zoom': ('Zoom extents', None, None, 'queued'),
}

# key -> what the toast line calls a count of that step
_SUMMARY = {
    'purge': 'purged',
    'zero_length': 'zero-length deleted',
    'duplicates': 'duplicates deleted',
    'bylayer': 'set to ByLayer',
}


def step_label(key):
    """The checklist label of a cleanup step."""
    for step in STEPS:
        if step[0] == key:
            return step[1]
    raise ValueError('%r is not a cleanup step; the steps are %s.'
                     % (key, ', '.join(step[0] for step in STEPS)))


def cleanup_plan(choices, defaults=None):
    """The cleanup steps to run, as keys in STEPS order.

    choices is what the person ticked: a list of keys (exactly those run), a
    dict of key to True/False (overriding defaults one step at a time), or
    None when nothing was chosen yet (the defaults run). defaults is a list
    of keys; None means the steps STEPS ticks by default. Unknown and
    repeated keys are dropped; case and surrounding spaces are ignored."""
    if defaults is None:
        chosen = set(step[0] for step in STEPS if step[2])
    else:
        chosen = set(_step_key(key) for key in defaults)
    if isinstance(choices, dict):
        for key, wanted in choices.items():
            if wanted:
                chosen.add(_step_key(key))
            else:
                chosen.discard(_step_key(key))
    elif choices is not None:
        chosen = set(_step_key(key) for key in choices)
    return [step[0] for step in STEPS if step[0] in chosen]


def deleting_steps(plan):
    """The steps in plan that erase something (purge, zero-length,
    duplicates), in run order: the ones to confirm before running."""
    return [key for key in cleanup_plan(list(plan)) if key in _DELETING]


def duplicate_extras(groups):
    """The handles to delete from duplicates() groups: every one but the
    first of each group, so one copy always survives."""
    extras = []
    for group in groups:
        extras.extend(list(group)[1:])
    return extras


def format_report(step_counts):
    """The Drawing Cleanup report: one line per step, in the order given.

    step_counts is a list of (key, result). result is a count, None for a
    step queued to run once the tool finishes (audit, zoom), or a dict with
    any of count, skipped (left alone on locked layers), groups (duplicate
    groups), error (why the step failed), cancelled or not_run."""
    if not step_counts:
        return 'No steps ran.'
    return '\n'.join(_report_line(key, result) for key, result in step_counts)


def summary(step_counts):
    """One line for the toast after a cleanup, such as '14 purged, 3
    zero-length deleted'; see format_report() for step_counts."""
    parts = []
    failed = 0
    cancelled = False
    for key, result in step_counts:
        info = _result(result)
        if info['error'] is not None:
            failed += 1
        elif info['cancelled']:
            cancelled = True
        elif info['count'] and key in _SUMMARY:
            parts.append('%d %s' % (info['count'], _SUMMARY[key]))
    if failed:
        parts.append(_plural(failed, 'step failed', 'steps failed'))
    if cancelled:
        parts.append('cancelled')
    if not parts:
        return 'Nothing needed cleaning'
    line = ', '.join(parts)
    return line[0].upper() + line[1:]


def _step_key(key):
    return str(key).strip().lower()


def _plural(count, one, many):
    return '%d %s' % (count, one if count == 1 else many)


def _result(result):
    """A step result as a dict with every field present."""
    info = {'count': None, 'skipped': 0, 'groups': None, 'error': None,
            'cancelled': False, 'not_run': False}
    if isinstance(result, dict):
        for key in info:
            if result.get(key) is not None:
                info[key] = result[key]
    elif result is not None and not isinstance(result, bool):
        info['count'] = int(result)
    return info


def _report_line(key, result):
    name, one, many, nothing = _REPORT.get(key, (str(key), None, None, ''))
    info = _result(result)
    count = info['count']
    if info['error'] is not None:
        body = 'failed (%s)' % info['error']
    elif info['cancelled']:
        body = 'cancelled'
    elif info['not_run']:
        body = 'not run'
    elif count is None:
        body = nothing
    elif one is None:
        body = str(count)
    elif count or info['skipped']:
        body = _plural(count, one, many) if count else 'none ' + many.split()[-1]
        if count and info['groups']:
            body += ' (%s)' % _plural(info['groups'], 'group', 'groups')
        if info['skipped']:
            body += ', %d skipped on locked layers' % info['skipped']
    else:
        body = nothing
    return '%s: %s' % (name, body)


# --- Font and Proxy Report: missing fonts, remapping, proxies ----------------

FONT_REPORT_HEADERS = ['Issue', 'Name', 'Detail', 'Where', 'Handle']

_TRUETYPE = ('.ttf', '.ttc', '.otf')
_REMAP_DEFAULTS = (('shx', 'simplex.shx'), ('ttf', 'arial.ttf'))
_ROLE_ISSUE = {'font': 'Missing font', 'big font': 'Missing big font',
               'shape': 'Missing shape file'}


def font_kind(font_file):
    """'ttf' for a TrueType or OpenType file (.ttf, .ttc, .otf), else 'shx'."""
    return 'ttf' if _font_name(font_file).endswith(_TRUETYPE) else 'shx'


def missing_fonts(styles, available):
    """The rows of styles whose font file is not available, in order.

    styles is a list of (style_name, font_file, ...) rows, such as
    text_style_fonts() returns; extra columns ride along untouched and rows
    with no font file are skipped. available is either a collection of font
    file names (compared case-insensitively on the bare file name, a name
    with no extension meaning .shx) or a callable taking a font file and
    returning True when it is found, asked once per distinct file."""
    if callable(available):
        cache = {}

        def found(font):
            key = str(font).lower()
            if key not in cache:
                cache[key] = bool(available(font))
            return cache[key]
    else:
        names = set(_font_name(name) for name in available)

        def found(font):
            return _font_name(font) in names
    return [row for row in styles if row[1] and not found(row[1])]


def remap_plan(missing_rows):
    """How to remap missing fonts: [(kind, default replacement, style
    names)], SHX first ('simplex.shx'), then TrueType ('arial.ttf'). Only a
    style's own font is remapped; big fonts and shape files are reported but
    left alone. A row with no third (role) column is a style's own font."""
    groups = {}
    for row in missing_rows:
        if _role(row) != 'font' or not row[1]:
            continue
        names = groups.setdefault(font_kind(row[1]), [])
        if row[0] not in names:
            names.append(row[0])
    return [(kind, default, groups[kind]) for kind, default in _REMAP_DEFAULTS
            if kind in groups]


def proxy_summary(details):
    """[(class, application, count)] from proxy_details() rows, most common
    first, then by class name. A proxy that names no class counts as
    '(unknown class)'."""
    counts = {}
    for item in details:
        key = (_proxy_class(item), str(item.get('application') or ''))
        counts[key] = counts.get(key, 0) + 1
    rows = [(key[0], key[1], count) for key, count in counts.items()]
    rows.sort(key=lambda row: (-row[2], row[0].lower(), row[1].lower()))
    return rows


def format_font_report(details, missing, limit=15):
    """The Font and Proxy Report text: proxies grouped by class (see
    proxy_summary()), then the missing font rows (see missing_fonts()).
    Each list stops after limit lines; the spreadsheet export carries
    everything."""
    lines = []
    if details:
        objects = len([item for item in details if item.get('kind') == 'object'])
        entities = len(details) - objects
        counts = []
        if entities:
            counts.append(_plural(entities, 'proxy entity', 'proxy entities'))
        if objects:
            counts.append(_plural(objects, 'proxy object', 'proxy objects'))
        lines.append(', '.join(counts))
        lines.extend(_cut(['  %s%s: %d' % (name, ' (%s)' % application if application else '', count)
                           for name, application, count in proxy_summary(details)], limit))
    else:
        lines.append('No proxies.')
    lines.append('')
    if missing:
        lines.append(_plural(len(missing), 'font', 'fonts') + ' not found in the support paths')
        rows = []
        for row in missing:
            role = _role(row)
            if role == 'shape':
                rows.append('  Shape file: %s' % row[1])
            elif role == 'big font':
                rows.append('  %s: %s (big font)' % (row[0], row[1]))
            else:
                rows.append('  %s: %s' % (row[0], row[1]))
        lines.extend(_cut(rows, limit))
    else:
        lines.append('Every font the text styles use was found.')
    return '\n'.join(lines)


def font_report_rows(details, missing):
    """Spreadsheet rows under FONT_REPORT_HEADERS: one per proxy, then one
    per missing font row."""
    rows = []
    for item in details:
        rows.append(['Proxy object' if item.get('kind') == 'object' else 'Proxy entity',
                     _proxy_class(item), str(item.get('application') or ''),
                     str(item.get('where') or ''), str(item.get('handle') or '')])
    for row in missing:
        rows.append([_ROLE_ISSUE.get(_role(row), 'Missing font'), str(row[0] or ''),
                     str(row[1]), '', ''])
    return rows


def _font_name(font_file):
    """A font file as its lowercased bare name; no extension means .shx."""
    name = str(font_file or '').replace('/', '\\').split('\\')[-1].strip().lower()
    if name and '.' not in name:
        name += '.shx'
    return name


def _role(row):
    return row[2] if len(row) > 2 else 'font'


def _proxy_class(item):
    return str(item.get('class') or '') or '(unknown class)'


def _cut(lines, limit):
    if limit and len(lines) > limit:
        return lines[:limit] + ['  ...and %d more' % (len(lines) - limit)]
    return lines


def purge(all=True):
    """Erases unreferenced blocks, layers, linetypes, text and dimension styles,
    registered applications, multiline, table and multileader styles; returns
    how many. all repeats until nothing is left (blocks nested in blocks)."""
    from pyacad import _api, doc, ents
    db = doc.database()
    tables = (db.BlockTableId, db.LayerTableId, db.LinetypeTableId, db.TextStyleTableId,
              db.DimStyleTableId, db.RegAppTableId)
    dictionaries = (db.MLStyleDictionaryId, db.TableStyleDictionaryId,
                    db.MLeaderStyleDictionaryId)
    removed = 0
    with doc.transaction() as tr:
        while True:
            ids = _api.DatabaseServices.ObjectIdCollection()
            for table_id in tables:
                for oid in ents._open_id(tr, table_id):
                    if not oid.IsErased:
                        ids.Add(oid)
            for dictionary_id in dictionaries:
                for entry in ents._open_id(tr, dictionary_id):
                    if not entry.Value.IsErased:
                        ids.Add(entry.Value)
            db.Purge(ids)
            if ids.Count == 0:
                break
            for oid in ids:
                ents._open_id(tr, oid, write=True).Erase()
            removed += ids.Count
            if not all:
                break
    return removed


def audit(fix=True):
    """Queues the AUDIT command (fixing errors when fix is True). It runs
    after the script returns, so nothing is reported back."""
    from pyacad import doc
    doc.send_command('_.AUDIT ' + ('_Y' if fix else '_N'))


def zero_length(progress=None, cancelled=None):
    """Handles of LINE and LWPOLYLINE entities with no length."""
    from pyacad import doc, ents
    found = []
    with doc.transaction() as tr:
        ids = _space_entities(tr, ('LINE', 'LWPOLYLINE'))
        for index, oid in enumerate(ids):
            if doc.step(index, len(ids), progress, cancelled):
                break
            if abs(float(ents._open_id(tr, oid).Length)) < 1e-12:
                found.append(ents._handle(oid))
    return found


def duplicates(tolerance=1e-6, progress=None, cancelled=None):
    """Groups (lists of handles, two or more each) of entities with the same
    type, layer and geometry; see geometry_key()."""
    from pyacad import doc, ents
    groups = {}
    order = []
    cache = {}
    with doc.transaction() as tr:
        ids = _space_entities(tr, tuple(_GEOMETRY))
        for index, oid in enumerate(ids):
            if doc.step(index, len(ids), progress, cancelled):
                break
            snap = ents._snapshot(tr, ents._open_id(tr, oid), cache)
            key = (snap['space'],) + geometry_key(snap, tolerance)
            if key not in groups:
                groups[key] = []
                order.append(key)
            groups[key].append(snap['handle'])
    return [groups[k] for k in order if len(groups[k]) > 1]


def proxies():
    """Handles of proxy entities (DXF name starting ACAD_PROXY), in every
    space and block definition."""
    from pyacad import doc, ents
    found = []
    with doc.transaction() as tr:
        for block_id in ents._local_block_ids(tr, doc.database()):
            for oid in ents._open_id(tr, block_id):
                if str(oid.ObjectClass.DxfName).upper().startswith('ACAD_PROXY'):
                    found.append(ents._handle(oid))
    return found


def text_styles_missing_fonts():
    """Names of text styles whose font file AutoCAD cannot find."""
    from pyacad import _api, doc, ents
    ds = _api.DatabaseServices
    db = doc.database()
    host = ds.HostApplicationServices.Current
    missing = []
    with doc.transaction() as tr:
        for oid in ents._open_id(tr, db.TextStyleTableId):
            style = ents._open_id(tr, oid)
            name = str(style.Name)
            font = str(style.FileName or '')
            if not font or style.IsDependent:
                continue
            if not _find_font(ds, host, db, font, style.IsShapeFile):
                missing.append(name)
    return missing


def text_style_fonts():
    """Every font file the drawing's own text styles name, as (style_name,
    font_file, role) rows: role 'font' for a style's font, 'big font' for
    its Asian big font, 'shape' for a shape file loaded for complex
    linetypes. Styles from xrefs are left out. Feed it to missing_fonts()."""
    from pyacad import doc, ents
    db = doc.database()
    rows = []
    with doc.transaction() as tr:
        for oid in ents._open_id(tr, db.TextStyleTableId):
            style = ents._open_id(tr, oid)
            if style.IsDependent:
                continue
            name = str(style.Name)
            font = str(style.FileName or '')
            big = str(style.BigFontFileName or '')
            if font:
                rows.append((name, font, 'shape' if style.IsShapeFile else 'font'))
            if big:
                rows.append((name, big, 'big font'))
    return rows


def font_finder():
    """A callable taking a font file name and returning True when AutoCAD
    finds it (the support paths, the drawing's folder, and the Windows fonts
    for TrueType): the available argument of missing_fonts()."""
    from pyacad import _api, doc
    ds = _api.DatabaseServices
    db = doc.database()
    host = ds.HostApplicationServices.Current

    def found(font_file):
        return bool(_find_font(ds, host, db, str(font_file), False))
    return found


def set_text_style_font(style_name, font_file):
    """Points a text style at another font file, such as simplex.shx or
    arial.ttf, clearing any TrueType typeface so the file is what counts.
    Text already drawn with the style shows the new font after a REGEN."""
    from pyacad import _api, doc, ents
    from Autodesk.AutoCAD.GraphicsInterface import FontDescriptor
    font_file = str(font_file or '').strip()
    if not font_file:
        raise ValueError('A font file is needed, such as simplex.shx or arial.ttf.')
    name = str(style_name)
    db = doc.database()
    with doc.transaction() as tr:
        table = ents._open_id(tr, db.TextStyleTableId)
        if not table.Has(name):
            raise ValueError('No text style is named %s.' % name)
        style = ents._open_id(tr, table[name], write=True)
        if style.IsShapeFile:
            raise ValueError('%s holds a shape file, not a text font.' % name)
        style.Font = FontDescriptor('', False, False, 0, 0)
        style.FileName = font_file


def proxy_details():
    """Every proxy in the drawing as a dict: handle, kind ('entity' or
    'object'), class (the original class name, '' when the proxy does not
    say), application (what made it) and where (the space, or 'BLOCK:Name',
    for an entity; 'Named objects' for an object). Entities come from every
    space and block definition, objects from the named object dictionary and
    the dictionaries inside it. Feed it to proxy_summary()."""
    from pyacad import doc, ents
    db = doc.database()
    found = []
    cache = {}
    with doc.transaction() as tr:
        for block_id in ents._local_block_ids(tr, db):
            where = ents._space_name(tr, block_id, cache)
            for oid in ents._open_id(tr, block_id):
                if _is_proxy(oid):
                    found.append(_proxy_row(tr, oid, 'entity', where))
        _dictionary_proxies(tr, db.NamedObjectsDictionaryId, found, set())
    return found


def erase(handles, progress=None, cancelled=None):
    """Erases entities by handle in one transaction, leaving any on a locked
    layer alone; returns (erased, skipped). Handles that no longer resolve,
    such as ones already erased, are passed over."""
    from pyacad import doc, ents
    handles = doc.as_names(handles)
    db = doc.database()
    erased = skipped = 0
    with doc.transaction() as tr:
        locked = _locked_layers(tr, db)
        for index, handle in enumerate(handles):
            if doc.step(index, len(handles), progress, cancelled):
                break
            try:
                oid = ents._id(db, handle)
            except ValueError:
                continue
            entity = ents._open_id(tr, oid)
            if ents._handle(entity.LayerId) in locked:
                skipped += 1
                continue
            entity.UpgradeOpen()
            entity.Erase()
            erased += 1
    return erased, skipped


def colors_to_bylayer(progress=None, cancelled=None):
    """Sets the colour of every entity in model space and every layout, and
    of the attributes on block references, to ByLayer; returns (changed,
    skipped on locked layers). Block definitions and viewports are left
    alone."""
    from pyacad import doc, ents
    db = doc.database()
    changed = skipped = 0
    with doc.transaction() as tr:
        locked = _locked_layers(tr, db)
        ids = []
        for space_id in ents._layout_space_ids(tr, db):
            for oid in ents._open_id(tr, space_id):
                if str(oid.ObjectClass.DxfName).upper() != 'VIEWPORT':
                    ids.append(oid)
        for index, oid in enumerate(ids):
            if doc.step(index, len(ids), progress, cancelled):
                break
            entity = ents._open_id(tr, oid)
            targets = [entity]
            if str(oid.ObjectClass.DxfName).upper() == 'INSERT':
                targets.extend(ents._open_id(tr, attribute_id)
                               for attribute_id in entity.AttributeCollection
                               if not attribute_id.IsErased)
            for target in targets:
                if target.Color.IsByLayer:
                    continue
                if ents._handle(target.LayerId) in locked:
                    skipped += 1
                    continue
                target.UpgradeOpen()
                target.ColorIndex = 256
                changed += 1
    return changed, skipped


def _find_font(ds, host, db, font, shape):
    """Where AutoCAD finds a font file, or '' when it cannot."""
    if font.lower().endswith(_TRUETYPE):
        hint = ds.FindFileHint.TrueTypeFontFile
    elif shape:
        hint = ds.FindFileHint.CompiledShapeFile
    else:
        hint = ds.FindFileHint.FontFile
    try:
        return str(host.FindFile(font, db, hint) or '')
    except Exception:
        return ''


def _locked_layers(tr, db):
    """Handles of the drawing's locked layers."""
    from pyacad import ents
    locked = set()
    for oid in ents._open_id(tr, db.LayerTableId):
        if ents._open_id(tr, oid).IsLocked:
            locked.add(ents._handle(oid))
    return locked


def _is_proxy(oid):
    return str(oid.ObjectClass.DxfName).upper().startswith('ACAD_PROXY')


def _dictionary_proxies(tr, dictionary_id, found, seen):
    """Adds the proxy objects in a dictionary, and in the dictionaries nested
    in it, to found."""
    from pyacad import ents
    key = ents._handle(dictionary_id)
    if key in seen:
        return
    seen.add(key)
    for entry in ents._open_id(tr, dictionary_id):
        oid = entry.Value
        if oid.IsNull or oid.IsErased:
            continue
        if _is_proxy(oid):
            found.append(_proxy_row(tr, oid, 'object', 'Named objects'))
        elif str(oid.ObjectClass.DxfName).upper() in ('DICTIONARY', 'ACDBDICTIONARYWDFLT'):
            _dictionary_proxies(tr, oid, found, seen)


def _proxy_row(tr, oid, kind, where):
    from pyacad import ents
    obj = ents._open_id(tr, oid)
    return {'handle': ents._handle(oid), 'kind': kind,
            'class': _text_property(obj, 'OriginalClassName'),
            'application': _text_property(obj, 'ApplicationDescription'),
            'where': where}


def _text_property(obj, name):
    """A .NET property as one line of text; '' when the object lacks it."""
    try:
        value = getattr(obj, name)
    except Exception:
        return ''
    return ' '.join(str(value or '').split())


def _space_entities(tr, types):
    """Ids of the entities of these DXF types in model space and every layout."""
    from pyacad import doc, ents
    wanted = [t.upper() for t in types]
    ids = []
    for space_id in ents._layout_space_ids(tr, doc.database()):
        for oid in ents._open_id(tr, space_id):
            if str(oid.ObjectClass.DxfName).upper() in wanted:
                ids.append(oid)
    return ids


def _snap(value, tolerance):
    if isinstance(value, bool) or value is None:
        return value
    if isinstance(value, (int, float)):
        return int(round(float(value) / tolerance))
    if isinstance(value, dict):
        return tuple(sorted((str(k), _snap(v, tolerance)) for k, v in value.items()))
    if isinstance(value, (list, tuple)):
        return tuple(_snap(v, tolerance) for v in value)
    return str(value)
