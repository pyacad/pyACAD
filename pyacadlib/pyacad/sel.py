"""Selection: the pickfirst set, interactive picks, and filtered SelectAll.

Everything works with handles (hex strings). build_filter() and value_kind()
are pure; the rest import the AutoCAD API inside the function. The pick_*
prompts need an interactive editor, so they return None or [] in Core Console.
"""

_INT16 = ((60, 79), (170, 179), (270, 289), (370, 389), (400, 409), (1060, 1070))
_INT32 = ((90, 99), (420, 429), (440, 449), (450, 459), (1071, 1071))
_DOUBLE = ((38, 59), (140, 149), (460, 469), (1040, 1042))
_POINT = ((10, 18), (110, 112), (210, 210), (1010, 1013))


def value_kind(code):
    """'int16', 'int32', 'double', 'point' or 'text': what a filter code takes."""
    code = int(code)
    for kind, ranges in (('int16', _INT16), ('int32', _INT32), ('double', _DOUBLE),
                         ('point', _POINT)):
        for low, high in ranges:
            if low <= code <= high:
                return kind
    return 'text'


def build_filter(type=None, layer=None, space=None, **more):
    """(code, value) pairs for a selection filter: type (one name or a list,
    any case), layer (wildcards allowed), space (a layout name, 'model' for
    model space) and extra codes as c62=1 style keywords."""
    pairs = []
    if type is not None:
        names = [type] if isinstance(type, str) else [str(t) for t in type]
        pairs.append((0, ','.join(n.strip().upper() for n in names)))
    if layer is not None:
        pairs.append((8, str(layer)))
    if space is not None:
        name = str(space)
        pairs.append((410, 'Model' if name.lower() == 'model' else name))
    extra = []
    for key, value in more.items():
        if len(key) < 2 or key[0] not in 'cC' or not key[1:].lstrip('-').isdigit():
            raise ValueError('Extra filter keywords look like c62=1, not %s.' % key)
        code = int(key[1:])
        kind = value_kind(code)
        if kind in ('int16', 'int32'):
            value = int(value)
        elif kind == 'double':
            value = float(value)
        elif kind == 'point':
            from pyacad import doc
            value = doc.as_point(value)
        else:
            value = str(value)
        extra.append((code, value))
    pairs.extend(sorted(extra, key=lambda pair: pair[0]))
    return pairs


def current():
    """Handles in the pickfirst set; [] when nothing is selected."""
    from pyacad import doc
    return _handles(doc.editor().SelectImplied())


def set_current(handles):
    """Makes these handles the pickfirst set."""
    from pyacad import _api, doc, ents
    import System
    db = doc.database()
    ids = [ents._id(db, h) for h in doc.as_names(handles)]
    doc.editor().SetImpliedSelection(System.Array[_api.DatabaseServices.ObjectId](ids))


def clear():
    """Empties the pickfirst set."""
    from pyacad import _api, doc
    import System
    doc.editor().SetImpliedSelection(System.Array[_api.DatabaseServices.ObjectId]([]))


def pick_one(prompt='Select an object: '):
    """Asks the user for one entity; its handle, or None on cancel."""
    from pyacad import _api, doc
    result = doc.editor().GetEntity(_api.EditorInput.PromptEntityOptions(str(prompt)))
    if result.Status != _api.EditorInput.PromptStatus.OK:
        return None
    return str(result.ObjectId.Handle.ToString())


def pick_many(prompt='Select objects: '):
    """Asks the user for a selection; a list of handles, [] on cancel."""
    from pyacad import _api, doc
    options = _api.EditorInput.PromptSelectionOptions()
    options.MessageForAdding = str(prompt)
    return _handles(doc.editor().GetSelection(options))


def pick_point(prompt='Pick a point: '):
    """Asks the user for a point; (x, y, z), or None on cancel."""
    from pyacad import _api, doc
    result = doc.editor().GetPoint(_api.EditorInput.PromptPointOptions(str(prompt)))
    if result.Status != _api.EditorInput.PromptStatus.OK:
        return None
    return doc._point(result.Value)


def get(type=None, layer=None, space=None, **more):
    """Handles of every entity that passes the filter (see build_filter)."""
    from pyacad import _api, doc
    import System
    ds = _api.DatabaseServices
    values = []
    for code, value in build_filter(type, layer, space, **more):
        kind = value_kind(code)
        if kind == 'int16':
            value = System.Int16(value)
        elif kind == 'int32':
            value = System.Int32(value)
        elif kind == 'double':
            value = System.Double(value)
        elif kind == 'point':
            value = doc._point3d(value)
        values.append(ds.TypedValue(code, value))
    editor = doc.editor()
    if values:
        result = editor.SelectAll(_api.EditorInput.SelectionFilter(System.Array[ds.TypedValue](values)))
    else:
        result = editor.SelectAll()
    return _handles(result)


def _handles(result):
    from pyacad import _api
    if result.Status != _api.EditorInput.PromptStatus.OK or result.Value is None:
        return []
    return [str(oid.Handle.ToString()) for oid in result.Value.GetObjectIds()]
