"""Entities by handle: iterate a space, read a snapshot, change, delete, measure.

Handles are hex strings such as '2F3A'. get() returns a plain dict, so a script
never holds an AutoCAD object past its transaction. The AutoCAD API is imported
inside each function; resolve_space(), normalize_handle(), check_props() and
length_by_layer() are pure.
"""

_COMMON_PROPS = ('layer', 'color', 'linetype', 'lineweight', 'visible')
_TYPE_PROPS = {
    'TEXT': ('text', 'height'),
    'MTEXT': ('text', 'height'),
    'INSERT': ('attributes',),
}
LINEWEIGHTS = (-3, -2, -1, 0, 5, 9, 13, 15, 18, 20, 25, 30, 35, 40, 50, 53, 60, 70,
               80, 90, 100, 106, 120, 140, 158, 200, 211)
# What a run-length takeoff measures: POLYLINE is both the old 2D and the 3D polyline.
LENGTH_TYPES = ('LINE', 'ARC', 'LWPOLYLINE', 'POLYLINE', 'SPLINE')


def resolve_space(text):
    """('model', 'Model'), ('layout', name) or ('block', name) for a space
    argument: None or 'model' is model space, 'BLOCK:Name' a block definition,
    anything else a paper space layout name."""
    if text is None:
        return ('model', 'Model')
    value = str(text).strip()
    if value == '' or value.lower() == 'model':
        return ('model', 'Model')
    if value[:6].upper() == 'BLOCK:':
        name = value[6:].strip()
        if not name:
            raise ValueError('BLOCK: needs a block name, as in "BLOCK:Door".')
        return ('block', name)
    return ('layout', value)


def normalize_handle(handle):
    """A handle as uppercase hex text; ValueError when it is not hex."""
    text = str(handle).strip().upper()
    if text.startswith('0X'):
        text = text[2:]
    if not text or any(c not in '0123456789ABCDEF' for c in text):
        raise ValueError('%r is not a handle (hex text such as "2F3A").' % (handle,))
    return text


def check_props(type_name, props):
    """Raises ValueError for a property set() cannot apply to this entity type."""
    allowed = _COMMON_PROPS + _TYPE_PROPS.get(str(type_name).upper(), ())
    for key in sorted(props):
        if key not in allowed:
            raise ValueError('%s cannot set %r; it takes %s.'
                             % (type_name, key, ', '.join(allowed)))
    if 'color' in props:
        color = props['color']
        if isinstance(color, bool) or not isinstance(color, int) or not 0 <= color <= 256:
            raise ValueError('color is a color index from 0 to 256, not %r.' % (color,))
    if 'lineweight' in props and props['lineweight'] not in LINEWEIGHTS:
        raise ValueError('lineweight %r is not one of %s.'
                         % (props['lineweight'], ', '.join(str(w) for w in LINEWEIGHTS)))
    if 'height' in props and not float(props['height']) > 0:
        raise ValueError('height must be above zero.')
    if 'attributes' in props and not isinstance(props['attributes'], dict):
        raise ValueError('attributes is a dict of tag to text.')


def length_by_layer(snapshots):
    """[{'layer', 'count', 'length'}] totals per layer of dicts carrying at least
    'layer' and 'length' (as lengths() gives), sorted by layer ignoring case;
    a dict whose length is None is left out."""
    totals = {}
    for snap in snapshots:
        length = snap.get('length')
        if length is None:
            continue
        layer = str(snap.get('layer', ''))
        count, total = totals.get(layer, (0, 0.0))
        totals[layer] = (count + 1, total + float(length))
    return [{'layer': layer, 'count': totals[layer][0], 'length': totals[layer][1]}
            for layer in sorted(totals, key=lambda name: (name.lower(), name))]


def iter(space=None):
    """Yields the handle of every entity in model space (default), a layout
    (its name) or a block definition ('BLOCK:Name')."""
    from pyacad import doc
    with doc.transaction() as tr:
        record = _open_id(tr, _space_id(tr, doc.database(), space))
        handles = [_handle(oid) for oid in record]
    for handle in handles:
        yield handle


def get(handle):
    """A plain dict snapshot of one entity: handle, type, layer, color,
    linetype, lineweight, visible, space, plus geometry for LINE, CIRCLE, ARC,
    LWPOLYLINE, TEXT, MTEXT (text is the raw Contents) and INSERT."""
    from pyacad import doc
    with doc.transaction() as tr:
        return _snapshot(tr, _open(tr, handle))


def set(handle, **props):
    """Changes layer, color, linetype, lineweight, visible; text and height on
    TEXT/MTEXT; attributes (dict tag to text) on INSERT."""
    from pyacad import doc
    with doc.transaction() as tr:
        entity = _open(tr, handle, write=True)
        type_name = str(entity.ObjectId.ObjectClass.DxfName)
        check_props(type_name, props)
        _apply(tr, doc.database(), entity, type_name, props)


def delete(handles, progress=None, cancelled=None):
    """Erases the entities in one transaction; returns how many were erased."""
    from pyacad import doc
    handles = doc.as_names(handles)
    erased = 0
    with doc.transaction() as tr:
        for index, handle in enumerate(handles):
            if doc.step(index, len(handles), progress, cancelled):
                break
            _open(tr, handle, write=True).Erase()
            erased += 1
    return erased


def count(space=None):
    """How many entities a space holds."""
    from pyacad import doc
    with doc.transaction() as tr:
        record = _open_id(tr, _space_id(tr, doc.database(), space))
        return sum(1 for _ in record)


def bounds(handle):
    """((minx, miny, minz), (maxx, maxy, maxz)) from the entity's extents."""
    from pyacad import doc
    with doc.transaction() as tr:
        extents = _open(tr, handle).GeometricExtents
        return (doc._point(extents.MinPoint), doc._point(extents.MaxPoint))


def type_of(handle):
    """The entity's DXF type name, such as 'LINE' or 'INSERT'."""
    from pyacad import doc
    return str(_id(doc.database(), handle).ObjectClass.DxfName)


def length_of(handle):
    """The length along a curve (line, arc, circle, polyline of any kind,
    spline, ellipse) in drawing units; ValueError for anything without one."""
    from pyacad import doc
    with doc.transaction() as tr:
        entity = _open(tr, handle)
        length = _curve_length(entity)
        if length is None:
            raise ValueError('%s (%s) has no length.'
                             % (handle, entity.ObjectId.ObjectClass.DxfName))
        return length


def lengths(handles, types=LENGTH_TYPES, progress=None, cancelled=None):
    """[{'handle', 'type', 'layer', 'length'}] for each of the handles whose DXF
    type is in types (lines, arcs, polylines and splines by default), read in
    one transaction. Other entities and handles that no longer exist are left
    out; a curve AutoCAD cannot measure gets length None."""
    from pyacad import doc
    wanted = tuple(str(t).upper() for t in types)   # not set(): this module defines set()
    db = doc.database()
    names = doc.as_names(handles)
    result = []
    with doc.transaction() as tr:
        for index, handle in enumerate(names):
            if doc.step(index, len(names), progress, cancelled):
                break
            try:
                oid = _id(db, handle)
            except ValueError:
                continue
            type_name = str(oid.ObjectClass.DxfName)
            if type_name not in wanted:
                continue
            entity = _open_id(tr, oid)
            result.append({'handle': _handle(oid), 'type': type_name,
                           'layer': str(entity.Layer), 'length': _curve_length(entity)})
    return result


def _curve_length(entity):
    """Length from start to end parameter, or None for a non-curve or a curve
    AutoCAD cannot measure (an xline, a degenerate spline)."""
    from pyacad import _api
    if not isinstance(entity, _api.DatabaseServices.Curve):
        return None
    try:
        return abs(float(entity.GetDistanceAtParameter(entity.EndParam))
                   - float(entity.GetDistanceAtParameter(entity.StartParam)))
    except Exception:
        return None


def _id(db, handle):
    from pyacad import _api
    import System
    value = _api.DatabaseServices.Handle(System.Int64(int(normalize_handle(handle), 16)))
    try:
        oid = db.GetObjectId(False, value, 0)
    except Exception:
        raise ValueError('No object has handle %s.' % handle)
    if oid.IsNull or oid.IsErased:
        raise ValueError('No object has handle %s.' % handle)
    return oid


def _open(tr, handle, write=False):
    from pyacad import doc
    return _open_id(tr, _id(doc.database(), handle), write)


def _open_id(tr, oid, write=False):
    from pyacad import _api
    mode = _api.DatabaseServices.OpenMode
    return tr.GetObject(oid, mode.ForWrite if write else mode.ForRead)


def _handle(oid):
    return str(oid.Handle.ToString())


def _int(value):
    import System
    return int(System.Convert.ToInt32(value))


def _enum(python_type, number):
    import clr
    import System
    return System.Enum.ToObject(clr.GetClrType(python_type), System.Int32(number))


def _space_id(tr, db, space):
    from pyacad import _api
    kind, name = resolve_space(space)
    table = _open_id(tr, db.BlockTableId)
    if kind == 'model':
        return table[_api.DatabaseServices.BlockTableRecord.ModelSpace]
    if kind == 'block':
        if not table.Has(name):
            raise ValueError('No block is named %s.' % name)
        return table[name]
    layouts = _open_id(tr, db.LayoutDictionaryId)
    if not layouts.Contains(name):
        raise ValueError('No layout is named %s.' % name)
    return _open_id(tr, layouts.GetAt(name)).BlockTableRecordId


def _layout_space_ids(tr, db):
    """Block table record ids of model space and every paper space layout."""
    ids = []
    for entry in _open_id(tr, db.LayoutDictionaryId):
        ids.append(_open_id(tr, entry.Value).BlockTableRecordId)
    return ids


def _local_block_ids(tr, db):
    """Every block table record of this drawing, without xrefs and their blocks."""
    ids = []
    for oid in _open_id(tr, db.BlockTableId):
        record = _open_id(tr, oid)
        if not record.IsFromExternalReference and not record.IsDependent:
            ids.append(oid)
    return ids


def _space_name(tr, btr_id, cache=None):
    key = _handle(btr_id)
    if cache is not None and key in cache:
        return cache[key]
    record = _open_id(tr, btr_id)
    if record.IsLayout:
        name = str(_open_id(tr, record.LayoutId).LayoutName)
    else:
        name = 'BLOCK:' + str(record.Name)
    if cache is not None:
        cache[key] = name
    return name


def _block_name(tr, reference):
    if reference.IsDynamicBlock:
        return str(_open_id(tr, reference.DynamicBlockTableRecord).Name)
    return str(reference.Name)


def _attributes(tr, reference):
    values = {}
    for oid in reference.AttributeCollection:
        if oid.IsErased:
            continue
        attribute = _open_id(tr, oid)
        values[str(attribute.Tag)] = str(attribute.TextString)
    return values


def _write_attributes(tr, reference, values):
    changed = 0
    for oid in reference.AttributeCollection:
        if oid.IsErased:
            continue
        attribute = _open_id(tr, oid)
        tag = str(attribute.Tag)
        if tag in values and str(attribute.TextString) != str(values[tag]):
            attribute.UpgradeOpen()
            attribute.TextString = str(values[tag])
            changed += 1
    return changed


def _snapshot(tr, entity, cache=None):
    from pyacad import doc
    p = doc._point
    type_name = str(entity.ObjectId.ObjectClass.DxfName)
    snap = {
        'handle': _handle(entity.ObjectId),
        'type': type_name,
        'layer': str(entity.Layer),
        'color': int(entity.ColorIndex),
        'linetype': str(entity.Linetype),
        'lineweight': _int(entity.LineWeight),
        'visible': bool(entity.Visible),
        'space': _space_name(tr, entity.BlockId, cache),
    }
    if type_name == 'LINE':
        snap['start'] = p(entity.StartPoint)
        snap['end'] = p(entity.EndPoint)
    elif type_name == 'CIRCLE':
        snap['center'] = p(entity.Center)
        snap['radius'] = float(entity.Radius)
    elif type_name == 'ARC':
        snap['center'] = p(entity.Center)
        snap['radius'] = float(entity.Radius)
        snap['start_angle'] = float(entity.StartAngle)
        snap['end_angle'] = float(entity.EndAngle)
    elif type_name == 'LWPOLYLINE':
        points = []
        for index in range(entity.NumberOfVertices):
            vertex = entity.GetPoint2dAt(index)
            points.append((float(vertex.X), float(vertex.Y)))
        snap['points'] = points
        snap['closed'] = bool(entity.Closed)
    elif type_name == 'TEXT':
        snap['text'] = str(entity.TextString)
        snap['height'] = float(entity.Height)
        snap['position'] = p(entity.Position)
    elif type_name == 'MTEXT':
        snap['text'] = str(entity.Contents)
        snap['height'] = float(entity.TextHeight)
        snap['position'] = p(entity.Location)
    elif type_name == 'INSERT':
        scale = entity.ScaleFactors
        snap['name'] = _block_name(tr, entity)
        snap['position'] = p(entity.Position)
        snap['rotation'] = float(entity.Rotation)
        snap['scale'] = (float(scale.X), float(scale.Y), float(scale.Z))
        snap['attributes'] = _attributes(tr, entity)
    return snap


def _apply(tr, db, entity, type_name, props):
    from pyacad import _api
    if 'layer' in props:
        name = str(props['layer'])
        if not _open_id(tr, db.LayerTableId).Has(name):
            raise ValueError('No layer is named %s.' % name)
        entity.Layer = name
    if 'color' in props:
        entity.ColorIndex = int(props['color'])
    if 'linetype' in props:
        name = str(props['linetype'])
        if name.lower() not in ('bylayer', 'byblock') and \
                not _open_id(tr, db.LinetypeTableId).Has(name):
            raise ValueError('No linetype is named %s.' % name)
        entity.Linetype = name
    if 'lineweight' in props:
        entity.LineWeight = _enum(_api.DatabaseServices.LineWeight, props['lineweight'])
    if 'visible' in props:
        entity.Visible = bool(props['visible'])
    if 'text' in props:
        if type_name == 'MTEXT':
            entity.Contents = str(props['text'])
        else:
            entity.TextString = str(props['text'])
    if 'height' in props:
        if type_name == 'MTEXT':
            entity.TextHeight = float(props['height'])
        else:
            entity.Height = float(props['height'])
    if 'attributes' in props:
        _write_attributes(tr, entity, props['attributes'])
