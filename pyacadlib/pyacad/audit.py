"""Drawing health: purge, audit, zero-length and duplicate geometry, proxies,
and text styles whose font files cannot be found.

geometry_key() is pure. Scans cover model space and every layout; proxies()
also looks inside block definitions. The AutoCAD API is imported inside each
function.
"""

_GEOMETRY = {
    'LINE': ('start', 'end'),
    'CIRCLE': ('center', 'radius'),
    'ARC': ('center', 'radius', 'start_angle', 'end_angle'),
    'LWPOLYLINE': ('points', 'closed'),
    'TEXT': ('text', 'height', 'position'),
    'MTEXT': ('text', 'height', 'position'),
    'INSERT': ('name', 'position', 'rotation', 'scale'),
}


def geometry_key(snapshot, tolerance=1e-6):
    """A hashable key for a pyacad.ents.get() snapshot: type, layer and
    geometry, with numbers snapped to tolerance. A LINE and its reverse share a
    key. None for types without a geometry rule."""
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
            if font.lower().endswith('.ttf') or font.lower().endswith('.ttc'):
                hint = ds.FindFileHint.TrueTypeFontFile
            elif style.IsShapeFile:
                hint = ds.FindFileHint.CompiledShapeFile
            else:
                hint = ds.FindFileHint.FontFile
            try:
                where = host.FindFile(font, db, hint)
            except Exception:
                where = ''
            if not where:
                missing.append(name)
    return missing


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
