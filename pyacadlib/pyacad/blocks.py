"""Blocks: definitions, references, attributes, counts, inserting and tables.

Dynamic block references report their definition's real name, not the
anonymous '*U' copy. Name arguments take '*' and '?' wildcards (see
pyacad.layers.matches). The AutoCAD API is imported inside each function.

The spreadsheet, schedule, find/replace and audit helpers (attribute_tags,
export_rows, diff_rows, schedule_rows, replace_in_attributes, tag_audit,
column_widths) are pure: they take the plain dicts snapshots() returns and give
plain lists and dicts back, so they run and test without AutoCAD.
"""

import math
import re

EXPORT_COLUMNS = ('Handle', 'Block', 'Layer', 'Space', 'X', 'Y', 'Z', 'Rotation', 'Scale')
_FIXED_COLUMNS = dict((name.lower(), name) for name in EXPORT_COLUMNS)


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


def snapshots(handles=None, progress=None, cancelled=None):
    """Snapshot dicts (as pyacad.ents.get gives) of block references, read in one
    transaction: every one in model space and the layouts when handles is None,
    else those of the handles that are block references (other entities and
    handles that no longer exist are left out). Xref attachments are never
    included. Each dict also carries 'tags', its attribute tags in drawing order."""
    from pyacad import doc, ents
    db = doc.database()
    result = []
    cache = {}
    xref_blocks = {}
    with doc.transaction() as tr:
        if handles is None:
            ids = [oid for space_id in ents._layout_space_ids(tr, db)
                   for oid in ents._open_id(tr, space_id)
                   if str(oid.ObjectClass.DxfName) == 'INSERT']
        else:
            ids = []
            for handle in doc.as_names(handles):
                try:
                    oid = ents._id(db, handle)
                except ValueError:
                    continue
                if str(oid.ObjectClass.DxfName) == 'INSERT':
                    ids.append(oid)
        for index, oid in enumerate(ids):
            if doc.step(index, len(ids), progress, cancelled):
                break
            reference = ents._open_id(tr, oid)
            if _is_xref(tr, reference, xref_blocks):
                continue
            snap = ents._snapshot(tr, reference, cache)
            snap['tags'] = _tag_list(tr, reference)
            result.append(snap)
    return result


def apply_changes(changes, progress=None, cancelled=None):
    """Writes {handle: {'attributes': {tag: text}, 'layer': name}} (either part
    optional, as diff_rows gives) into block references in one transaction.
    Returns {'updated': references changed, 'values': attribute values changed,
    'failed': {handle: reason}}; a reference that fails (gone, not a block,
    unknown layer, locked layer) is left as it was and the rest still go in."""
    _check_changes(changes)
    from pyacad import doc, ents
    db = doc.database()
    updated, values, failed = 0, 0, {}
    handles = sorted(changes)
    with doc.transaction() as tr:
        layer_table = ents._open_id(tr, db.LayerTableId)
        for index, handle in enumerate(handles):
            if doc.step(index, len(handles), progress, cancelled):
                break
            change = changes[handle]
            layer = change.get('layer')
            try:
                reference = _reference(tr, handle)
                if layer is not None and not layer_table.Has(str(layer)):
                    raise ValueError('No layer is named %s.' % layer)
                touched = False
                if layer is not None and str(reference.Layer) != str(layer):
                    reference.UpgradeOpen()
                    reference.Layer = str(layer)
                    touched = True
                count = ents._write_attributes(tr, reference, change.get('attributes') or {})
            except Exception as error:
                failed[handle] = _reason(error)
                continue
            values += count
            if touched or count:
                updated += 1
    return {'updated': updated, 'values': values, 'failed': failed}


def insert_table(point, headers, rows, title=None):
    """Inserts an AutoCAD table with its top left corner at point, in the current
    space and the drawing's current table style: an optional title row across the
    top, a header row, then one row per item. Every cell is written as text,
    columns are sized to their longest text. Returns the table's handle."""
    from pyacad import doc
    headers = [_text(h) for h in headers]
    if not headers:
        raise ValueError('A table needs at least one column.')
    cells = []
    for row in rows:
        row = list(row)
        if len(row) > len(headers):
            raise ValueError('A row has %d cells but the table has %d columns.'
                             % (len(row), len(headers)))
        cells.append([_text(v) for v in row] + [''] * (len(headers) - len(row)))
    position = doc.as_point(point)
    from pyacad import _api, ents
    ds = _api.DatabaseServices
    db = doc.database()
    columns = len(headers)
    top = 0 if title is None else 1
    with doc.transaction() as tr:
        table = ds.Table()
        table.TableStyle = db.Tablestyle
        table.SetSize(top + 1 + len(cells), columns)
        table.Position = doc._point3d(position)
        if title is not None:
            table.Rows[0].Style = '_TITLE'
            table.Cells[0, 0].TextString = _text(title)
            if columns > 1 and not table.Cells[0, 0].IsMerged:
                table.MergeCells(ds.CellRange.Create(table, 0, 0, 0, columns - 1))
        table.Rows[top].Style = '_HEADER'
        if table.Cells[top, 0].IsMerged:
            # The style merges its first row as a title; with no title that row
            # holds the headers and must stay one cell per column.
            table.UnmergeCells(table.Cells[top, 0].GetMergeRange())
        for column, text in enumerate(headers):
            table.Cells[top, column].TextString = text
        for offset, row in enumerate(cells):
            table.Rows[top + 1 + offset].Style = '_DATA'
            for column, text in enumerate(row):
                table.Cells[top + 1 + offset, column].TextString = text
        height, margin = _table_text_size(tr, table, db)
        title_height = _table_text_size(tr, table, db, '_TITLE')[0]
        widths = column_widths(headers, cells, height, margin, title, title_height)
        for column, width in enumerate(widths):
            table.Columns[column].Width = width
        table.GenerateLayout()
        space = ents._open_id(tr, db.CurrentSpaceId, write=True)
        space.AppendEntity(table)
        tr.AddNewlyCreatedDBObject(table, True)
        return ents._handle(table.ObjectId)


# --- pure: spreadsheets, schedules, find/replace and audits over snapshots ----

def attribute_tags(refs):
    """Every attribute tag across the refs, once each, in the order first met:
    a ref's 'tags' list when it has one (snapshots() gives the drawing's own
    order), else its tags sorted. Tags differing only in case count once,
    spelled as first met."""
    seen = set()
    result = []
    for ref in refs:
        for tag in _tag_order(ref):
            if tag.upper() not in seen:
                seen.add(tag.upper())
                result.append(tag)
    return result


def export_rows(refs):
    """(headers, rows) for a spreadsheet of block references: EXPORT_COLUMNS,
    then one column per attribute tag (attribute_tags order); one row per
    reference, sorted by block name then handle. Rotation is in degrees (0 up
    to 360), Scale one number when uniform else 'x, y, z' text, and a tag the
    reference does not have is an empty string."""
    from pyacad import doc
    tags = attribute_tags(refs)
    rows = []
    for ref in sorted(refs, key=_export_key):
        values = _by_upper_tag(ref)
        x, y, z = doc.as_point(ref.get('position') or (0.0, 0.0, 0.0))
        row = [str(ref.get('handle', '')), str(ref.get('name', '')),
               str(ref.get('layer', '')), str(ref.get('space', '')), x, y, z,
               _degrees(ref.get('rotation') or 0.0), _scale_cell(ref.get('scale', 1.0))]
        row.extend(values.get(tag.upper(), ('', ''))[1] for tag in tags)
        rows.append(row)
    return list(EXPORT_COLUMNS) + tags, rows


def diff_rows(current_refs, sheet_rows):
    """Compares a sheet written from export_rows (header row first, as
    pyacad.xl.read returns it) with the block references in the drawing now.

    Columns are found by header name, so a reordered sheet still reads. Only
    attribute values and Layer go back: a tag column the reference has is a
    change when its text differs (an emptied cell clears the value), a Layer
    cell when it is filled in and differs. Returns (changes, skipped): changes
    maps handle to {'attributes': {tag: text}, 'layer': name}, holding only the
    parts that changed; skipped lists, in sheet order, the sheet's handles that
    are no longer block references in current_refs. Rows with no handle are
    ignored."""
    from pyacad import ents
    if not sheet_rows:
        raise ValueError('The sheet is empty.')
    header = [_text(cell).strip() for cell in sheet_rows[0]]
    fixed = {}
    for index, name in enumerate(header):
        if name.lower() in _FIXED_COLUMNS and name.lower() not in fixed:
            fixed[name.lower()] = index
    if 'handle' not in fixed:
        raise ValueError('The sheet has no Handle column, so its rows cannot be '
                         'matched to blocks. Export one with Blocks to Excel.')
    tag_columns = [(index, name) for index, name in enumerate(header)
                   if name and index not in fixed.values()]
    current = {}
    for ref in current_refs:
        try:
            current[ents.normalize_handle(ref.get('handle', ''))] = ref
        except ValueError:
            continue
    changes, skipped = {}, []
    for row in sheet_rows[1:]:
        text = _handle_text(_cell(row, fixed['handle']))
        if not text:
            continue
        try:
            handle = ents.normalize_handle(text)
        except ValueError:
            handle = text
        ref = current.get(handle)
        if ref is None:
            if handle not in skipped:
                skipped.append(handle)
            continue
        values = _by_upper_tag(ref)
        new_values = {}
        for index, name in tag_columns:
            if name.upper() not in values:
                continue
            tag, old = values[name.upper()]
            value = _text(_cell(row, index))
            if value != old:
                new_values[tag] = value
        change = {}
        if new_values:
            change['attributes'] = new_values
        if 'layer' in fixed:
            layer = _text(_cell(row, fixed['layer'])).strip()
            if layer and layer != str(ref.get('layer', '')):
                change['layer'] = layer
        if change:
            merged = changes.setdefault(handle, {})
            merged.setdefault('attributes', {}).update(change.get('attributes', {}))
            if not merged['attributes']:
                del merged['attributes']
            if 'layer' in change:
                merged['layer'] = change['layer']
    return changes, skipped


def schedule_rows(refs, by_layer=False):
    """(headers, rows) counting block references by block name, or by block
    name and layer: ['Block', 'Count'] or ['Block', 'Layer', 'Count'], rows
    sorted by name then layer ignoring case, counts as ints."""
    counts = {}
    for ref in refs:
        key = (str(ref.get('name', '')),)
        if by_layer:
            key += (str(ref.get('layer', '')),)
        counts[key] = counts.get(key, 0) + 1
    order = sorted(counts, key=lambda k: tuple(part.lower() for part in k) + k)
    headers = ['Block', 'Layer', 'Count'] if by_layer else ['Block', 'Count']
    return headers, [list(key) + [counts[key]] for key in order]


def replace_in_attributes(refs, find, replace, case_sensitive=True, tag=None):
    """{handle: {tag: new text}} for every attribute value that changes when find
    becomes replace: only values that change, only the given tag (any case)
    when tag is set. find and replace are plain text, never patterns."""
    find = '' if find is None else str(find)
    if not find:
        raise ValueError('The text to find is empty.')
    replace = '' if replace is None else str(replace)
    wanted = None if tag is None or not str(tag).strip() else str(tag).strip().upper()
    pattern = None if case_sensitive else re.compile(re.escape(find), re.IGNORECASE)
    result = {}
    for ref in refs:
        changed = {}
        for name, value in (ref.get('attributes') or {}).items():
            if wanted is not None and str(name).upper() != wanted:
                continue
            value = str(value)
            if pattern is None:
                new = value.replace(find, replace)
            else:
                new = pattern.sub(lambda match: replace, value)
            if new != value:
                changed[str(name)] = new
        if changed:
            result[str(ref.get('handle', ''))] = changed
    return result


def tag_audit(refs, tag, ignore_case=True):
    """Audits one attribute tag (matched in any case) across block references:
    {'duplicates': {value: [handles]} for values two or more references share,
    'blanks': handles whose value is empty or spaces, 'missing': handles of
    references without that attribute, 'tagged': how many have a value}.
    Values are compared trimmed, and ignoring case unless ignore_case is False;
    a duplicate is keyed by its first spelling, handles in the refs' order."""
    wanted = '' if tag is None else str(tag).strip().upper()
    if not wanted:
        raise ValueError('Name the attribute tag to audit.')
    groups, order = {}, []
    blanks, missing = [], []
    tagged = 0
    for ref in refs:
        handle = str(ref.get('handle', ''))
        values = _by_upper_tag(ref)
        if wanted not in values:
            missing.append(handle)
            continue
        value = values[wanted][1].strip()
        if not value:
            blanks.append(handle)
            continue
        tagged += 1
        key = value.upper() if ignore_case else value
        if key not in groups:
            groups[key] = (value, [])
            order.append(key)
        groups[key][1].append(handle)
    duplicates = {}
    for key in order:
        spelling, handles = groups[key]
        if len(handles) > 1:
            duplicates[spelling] = handles
    return {'duplicates': duplicates, 'blanks': blanks, 'missing': missing, 'tagged': tagged}


def column_widths(headers, rows, text_height, margin=None, title=None, title_height=None):
    """Table column widths that fit each column's longest text (header or cell,
    at least 3 characters): characters x text_height x 0.9 plus a margin each
    side, the margin a third of the text height unless given. With a title, the
    columns share out any extra width the title needs to stay on one line."""
    height = float(text_height)
    if not height > 0:
        raise ValueError('text_height must be above zero.')
    pad = height / 3.0 if margin is None else float(margin)
    widths = []
    for index, header in enumerate(headers):
        chars = max([3, len(_text(header))] + [len(_text(_cell(row, index))) for row in rows])
        widths.append(chars * height * 0.9 + 2 * pad)
    if title and widths:
        size = height if title_height is None else float(title_height)
        short = len(_text(title)) * size * 0.9 + 2 * pad - sum(widths)
        if short > 0:
            widths = [w + short / len(widths) for w in widths]
    return widths


def _text(value):
    """A cell value as text: None empty, whole floats without '.0', other floats
    to 15 significant digits (what Excel keeps), anything else str()."""
    if value is None:
        return ''
    if isinstance(value, bool):
        return 'TRUE' if value else 'FALSE'
    if isinstance(value, float):
        if value == int(value):
            return '%d' % int(value)
        return '%.15g' % value
    return str(value)


def _handle_text(value):
    return _text(value).strip()


def _cell(row, index):
    return row[index] if index < len(row) else ''


def _tag_order(ref):
    """A ref's tags: its 'tags' order, then any other attribute keys sorted."""
    attributes = ref.get('attributes') or {}
    order = [str(t) for t in (ref.get('tags') or [])]
    listed = set(order)
    return order + sorted(str(t) for t in attributes if str(t) not in listed)


def _by_upper_tag(ref):
    """{TAG: (tag as spelled, value)} of a ref's attributes."""
    result = {}
    for tag, value in (ref.get('attributes') or {}).items():
        result[str(tag).upper()] = (str(tag), str(value))
    return result


def _export_key(ref):
    from pyacad import ents
    name = str(ref.get('name', ''))
    try:
        number = int(ents.normalize_handle(ref.get('handle', '')), 16)
    except ValueError:
        number = -1
    return (name.lower(), name, number)


def _degrees(radians):
    value = round(math.degrees(float(radians)) % 360.0, 6)
    return 0.0 if value >= 360.0 else value


def _scale_cell(scale):
    factors = _scale(scale)
    if max(factors) - min(factors) <= 1e-9 * max(1.0, abs(factors[0])):
        return factors[0]
    return ', '.join(_text(f) for f in factors)


def _check_changes(changes):
    if not isinstance(changes, dict):
        raise ValueError('changes is a dict of handle to {"attributes": ..., "layer": ...}.')
    for handle, change in changes.items():
        if not isinstance(change, dict):
            raise ValueError('The change for %s is not a dict.' % handle)
        if not isinstance(change.get('attributes') or {}, dict):
            raise ValueError('The attributes for %s are not a dict of tag to text.' % handle)


def _reason(error):
    text = str(error).strip()
    return text or error.__class__.__name__


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


def _is_xref(tr, reference, cache):
    """True when the reference is an xref attachment (cached per block record)."""
    from pyacad import ents
    key = ents._handle(reference.BlockTableRecord)
    if key not in cache:
        record = ents._open_id(tr, reference.BlockTableRecord)
        cache[key] = bool(record.IsFromExternalReference)
    return cache[key]


def _tag_list(tr, reference):
    """The reference's attribute tags in the order the drawing holds them."""
    from pyacad import ents
    tags = []
    for oid in reference.AttributeCollection:
        if not oid.IsErased:
            tags.append(str(ents._open_id(tr, oid).Tag))
    return tags


def _table_text_size(tr, table, db, cell_style='_DATA'):
    """(text height, horizontal cell margin) of one of the table style's cell
    styles; TEXTSIZE and a third of it when the style cannot say."""
    from pyacad import ents
    try:
        style = ents._open_id(tr, table.TableStyle)
        height = float(style.TextHeight(cell_style))
        if height > 0:
            return height, float(style.HorizontalCellMargin)
    except Exception:
        pass
    height = float(db.Textsize) or 2.5
    return height, height / 3.0


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
