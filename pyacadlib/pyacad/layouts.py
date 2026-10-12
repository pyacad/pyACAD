"""Layouts: paper space tabs in order, the current one, create, copy, delete,
rename, reorder, and the drawing's named page setups.

Changes go through LayoutManager, which acts on the working database (the
active drawing in a command). is_model(), reorder() and unique_name() are pure.
The AutoCAD API is imported inside each function.
"""

import re as _re

# A trailing copy counter such as ' (2)', so copying 'A1 (2)' gives 'A1 (3)'.
_COUNTER = _re.compile(r'^(.*\S)\s*\((\d+)\)$')


def is_model(name):
    """True for the model tab's name, in any case."""
    return str(name).strip().lower() == 'model'


def reorder(names, name, new_index):
    """names (a tab order) with name moved to new_index (0 is first), as a new
    list. The index is clamped to the list; name matches in any case;
    ValueError when it is not in names."""
    order = [str(n) for n in names]
    index = _index_of(order, name)
    if index is None:
        raise ValueError('No layout is named %s.' % name)
    moved = order.pop(index)
    order.insert(max(0, min(int(new_index), len(order))), moved)
    return order


def unique_name(existing, wanted):
    """wanted, or wanted with ' (2)', ' (3)' ... when a layout in existing (any
    case) or the model tab already has that name. A counter wanted already
    carries is replaced, not stacked: 'A1 (2)' becomes 'A1 (3)'."""
    name = str(wanted).strip()
    if not name:
        raise ValueError('A layout needs a name.')
    taken = set(str(n).strip().lower() for n in existing)
    taken.add('model')
    if name.lower() not in taken:
        return name
    match = _COUNTER.match(name)
    base = match.group(1) if match else name
    count = 2
    while ('%s (%d)' % (base, count)).lower() in taken:
        count += 1
    return '%s (%d)' % (base, count)


def _index_of(order, name):
    """Position of name in order: an exact match first, else any case."""
    text = str(name)
    if text in order:
        return order.index(text)
    folded = [n.lower() for n in order]
    return folded.index(text.lower()) if text.lower() in folded else None


def names():
    """Paper space layout names, in tab order."""
    from pyacad import doc, ents
    found = []
    with doc.transaction() as tr:
        for entry in ents._open_id(tr, doc.database().LayoutDictionaryId):
            layout = ents._open_id(tr, entry.Value)
            if not layout.ModelType:
                found.append((int(layout.TabOrder), str(layout.LayoutName)))
    return [name for _, name in sorted(found)]


def current():
    """The current layout's name ('Model' on the model tab)."""
    return str(_manager().CurrentLayout)


def set_current(name):
    """Switches to the named layout ('Model' for the model tab)."""
    if not exists(name):
        raise ValueError('No layout is named %s.' % name)
    _manager().CurrentLayout = 'Model' if is_model(name) else str(name)


def exists(name):
    """True when the drawing has a layout of that name."""
    return bool(_manager().LayoutExists(str(name)))


def create(name):
    """Adds a paper space layout; returns its name."""
    if exists(name):
        raise ValueError('Layout %s already exists.' % name)
    _manager().CreateLayout(str(name))
    return str(name)


def delete(name):
    """Deletes a paper space layout (never Model)."""
    if is_model(name):
        raise ValueError('The model tab cannot be deleted.')
    if not exists(name):
        raise ValueError('No layout is named %s.' % name)
    _manager().DeleteLayout(str(name))


def rename(old, new):
    """Renames a paper space layout (a change of case only is allowed)."""
    if is_model(old) or is_model(new):
        raise ValueError('The model tab cannot be renamed.')
    if not exists(old):
        raise ValueError('No layout is named %s.' % old)
    if exists(new) and str(new).lower() != str(old).lower():
        raise ValueError('Layout %s already exists.' % new)
    _manager().RenameLayout(str(old), str(new))


def copy(source, new):
    """Copies a paper space layout, with its content and plot settings, to a
    new layout named new; returns that name. unique_name() picks a free one."""
    if is_model(source) or is_model(new):
        raise ValueError('The model tab cannot be copied.')
    name = str(new).strip()
    if not name:
        raise ValueError('The copy needs a name.')
    if not exists(source):
        raise ValueError('No layout is named %s.' % source)
    if exists(name):
        raise ValueError('Layout %s already exists.' % name)
    _manager().CopyLayout(str(source), name)
    return name


def set_order(ordered):
    """Puts the paper space layouts in this tab order. ordered names every
    layout exactly once, in any case, as reorder() returns it."""
    from pyacad import doc, ents
    current = names()
    actual = dict((n.lower(), n) for n in current)
    wanted = [str(n) for n in ordered]
    if sorted(n.lower() for n in wanted) != sorted(actual):
        raise ValueError('The new order must name every layout once: %s.' % ', '.join(current))
    db = doc.database()
    with doc.transaction() as tr:
        layouts = ents._open_id(tr, db.LayoutDictionaryId)
        for position, name in enumerate(wanted, 1):
            oid = layouts.GetAt(actual[name.lower()])
            if int(ents._open_id(tr, oid).TabOrder) != position:
                ents._open_id(tr, oid, write=True).TabOrder = position


def page_setups(paper_only=False):
    """Names of the drawing's named page setups, sorted. paper_only leaves out
    the ones made for model space, which a paper space layout cannot take."""
    from pyacad import doc, ents
    found = []
    with doc.transaction() as tr:
        setups = ents._open_id(tr, doc.database().PlotSettingsDictionaryId)
        for entry in setups:
            if paper_only and ents._open_id(tr, entry.Value).ModelType:
                continue
            found.append(str(entry.Key))
    return sorted(found, key=lambda n: n.lower())


def apply_page_setup(setup, layout_names):
    """Makes a named paper space page setup the plot settings of each layout
    named; returns how many. All or nothing: a bad name changes no layout."""
    from pyacad import doc, ents
    db = doc.database()
    with doc.transaction() as tr:
        setups = ents._open_id(tr, db.PlotSettingsDictionaryId)
        if not setups.Contains(str(setup)):
            raise ValueError('No page setup is named %s.' % setup)
        source = ents._open_id(tr, setups.GetAt(str(setup)))
        if source.ModelType:
            raise ValueError('%s is a model space page setup; a layout cannot take it.' % setup)
        layouts = ents._open_id(tr, db.LayoutDictionaryId)
        ids = []
        for name in [str(n) for n in layout_names]:
            if is_model(name) or not layouts.Contains(name):
                raise ValueError('No layout is named %s.' % name)
            ids.append(layouts.GetAt(name))
        for oid in ids:
            ents._open_id(tr, oid, write=True).CopyFrom(source)
    return len(ids)


def _manager():
    from pyacad import _api, doc
    doc.active()
    return _api.DatabaseServices.LayoutManager.Current
