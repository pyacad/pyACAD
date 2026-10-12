"""Blocks: definitions, references, attributes, counts and inserting.

Dynamic block references report their definition's real name, not the
anonymous '*U' copy. Name arguments take '*' and '?' wildcards (see
pyacad.layers.matches). The AutoCAD API is imported inside each function.
"""


def definitions():
    """Names of user blocks: no layouts, anonymous blocks or xref blocks."""
    from pyacad import doc, ents
    db = doc.database()
    result = []
    with doc.transaction() as tr:
        for oid in ents._open_id(tr, db.BlockTableId):
            record = ents._open_id(tr, oid)
            if record.IsLayout or record.IsAnonymous or record.IsDependent \
                    or record.IsFromExternalReference:
                continue
            result.append(str(record.Name))
    return sorted(result, key=lambda n: n.lower())


def references(name=None, space=None):
    """Handles of block references: all, or those whose block name matches.
    space is None for model space and every layout, or one space as in
    pyacad.ents.iter."""
    from pyacad import doc
    with doc.transaction() as tr:
        return [handle for handle, _ in _inserts(tr, name, space)]


def attributes(handle):
    """{tag: text} of a block reference's attributes."""
    from pyacad import doc, ents
    with doc.transaction() as tr:
        return ents._attributes(tr, _reference(tr, handle))


def set_attributes(handle, values):
    """Writes {tag: text} into a block reference; returns how many changed."""
    from pyacad import doc, ents
    if not isinstance(values, dict):
        raise ValueError('values is a dict of tag to text.')
    with doc.transaction() as tr:
        return ents._write_attributes(tr, _reference(tr, handle), values)


def counts(space=None):
    """{block name: how many references} in a space (None: every layout)."""
    from pyacad import doc
    result = {}
    with doc.transaction() as tr:
        for _, block in _inserts(tr, None, space):
            result[block] = result.get(block, 0) + 1
    return result


def insert(name, point, rotation=0.0, scale=1.0, layer=None, attributes=None):
    """Inserts a block into the current space with its attributes (values
    from attributes, else the definition's defaults); returns the handle."""
    from pyacad import _api, doc, ents
    ds = _api.DatabaseServices
    position = doc._point3d(point)
    factors = _scale(scale)
    values = attributes or {}
    db = doc.database()
    with doc.transaction() as tr:
        table = ents._open_id(tr, db.BlockTableId)
        if not table.Has(str(name)):
            raise ValueError('No block is named %s.' % name)
        block_id = table[str(name)]
        if layer is not None and not ents._open_id(tr, db.LayerTableId).Has(str(layer)):
            raise ValueError('No layer is named %s.' % layer)
        space = ents._open_id(tr, db.CurrentSpaceId, write=True)
        reference = ds.BlockReference(position, block_id)
        reference.Rotation = float(rotation)
        reference.ScaleFactors = _api.Geometry.Scale3d(*factors)
        if layer is not None:
            reference.Layer = str(layer)
        space.AppendEntity(reference)
        tr.AddNewlyCreatedDBObject(reference, True)
        for oid in ents._open_id(tr, block_id):
            if str(oid.ObjectClass.DxfName) != 'ATTDEF':
                continue
            definition = ents._open_id(tr, oid)
            if definition.Constant:
                continue
            attribute = ds.AttributeReference()
            attribute.SetAttributeFromBlock(definition, reference.BlockTransform)
            tag = str(definition.Tag)
            if tag in values:
                attribute.TextString = str(values[tag])
            reference.AttributeCollection.AppendAttribute(attribute)
            tr.AddNewlyCreatedDBObject(attribute, True)
        return ents._handle(reference.ObjectId)


def definition_info(name):
    """{name, has_attributes, is_dynamic, origin, comments} of a block."""
    from pyacad import doc, ents
    with doc.transaction() as tr:
        table = ents._open_id(tr, doc.database().BlockTableId)
        if not table.Has(str(name)):
            raise ValueError('No block is named %s.' % name)
        record = ents._open_id(tr, table[str(name)])
        return {
            'name': str(record.Name),
            'has_attributes': bool(record.HasAttributeDefinitions),
            'is_dynamic': bool(record.IsDynamicBlock),
            'origin': doc._point(record.Origin),
            'comments': str(record.Comments or ''),
        }


def _scale(scale):
    """(x, y, z) scale factors from one number or a 3-tuple."""
    if isinstance(scale, (int, float)):
        return (float(scale),) * 3
    factors = tuple(float(s) for s in scale)
    if len(factors) != 3:
        raise ValueError('scale is one number or (x, y, z).')
    return factors


def _reference(tr, handle):
    from pyacad import ents
    entity = ents._open(tr, handle)
    if str(entity.ObjectId.ObjectClass.DxfName) != 'INSERT':
        raise ValueError('%s is not a block reference.' % handle)
    return entity


def _inserts(tr, name, space):
    """(handle, block name) of each INSERT in the space(s) whose name matches."""
    from pyacad import doc, ents, layers
    db = doc.database()
    if space is None:
        spaces = ents._layout_space_ids(tr, db)
    else:
        spaces = [ents._space_id(tr, db, space)]
    result = []
    for space_id in spaces:
        for oid in ents._open_id(tr, space_id):
            if str(oid.ObjectClass.DxfName) != 'INSERT':
                continue
            block = ents._block_name(tr, ents._open_id(tr, oid))
            if name is None or layers.matches_any(block, name):
                result.append((ents._handle(oid), block))
    return result
