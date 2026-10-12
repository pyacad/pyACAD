"""Layouts: paper space tabs in order, the current one, create, delete, rename,
and the drawing's named page setups.

Changes go through LayoutManager, which acts on the working database (the
active drawing in a command). is_model() is pure. The AutoCAD API is imported
inside each function.
"""


def is_model(name):
    """True for the model tab's name, in any case."""
    return str(name).strip().lower() == 'model'


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
    """Renames a paper space layout."""
    if is_model(old) or is_model(new):
        raise ValueError('The model tab cannot be renamed.')
    if not exists(old):
        raise ValueError('No layout is named %s.' % old)
    if exists(new):
        raise ValueError('Layout %s already exists.' % new)
    _manager().RenameLayout(str(old), str(new))


def page_setups():
    """Names of the drawing's named page setups."""
    from pyacad import doc, ents
    with doc.transaction() as tr:
        setups = ents._open_id(tr, doc.database().PlotSettingsDictionaryId)
        return sorted((str(entry.Key) for entry in setups), key=lambda n: n.lower())


def _manager():
    from pyacad import _api, doc
    doc.active()
    return _api.DatabaseServices.LayoutManager.Current
