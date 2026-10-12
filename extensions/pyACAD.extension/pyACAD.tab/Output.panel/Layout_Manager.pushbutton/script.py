# -*- coding: utf-8 -*-
"""Renames, reorders, copies and deletes paper space layouts, and applies one
named page setup to many layouts at once.

One action per run, picked from the first dialog. Each run is one AutoCAD
command, so a single Undo takes the change back.
"""

from pyacad import doc, forms, layouts, script, toast

__title__ = 'Layout Manager'

_TITLE = 'Layout manager'


def run():
    if not _has_drawing():
        toast.warning('No drawing is open', 'Open a drawing and run Layout Manager again.')
        return
    names = layouts.names()
    if not names:
        toast.info('No layouts', 'This drawing has only the model tab.')
        return
    action = forms.ask_options('What do you want to do with the layouts?',
                               [label for label, _ in _ACTIONS], _TITLE)
    if action is None:
        return
    handler = dict(_ACTIONS)[action]
    try:
        handler(names)
    except Exception as error:
        script.get_logger().error('layout manager: %s failed: %s' % (action, error))
        toast.error('Could not %s' % action.lower(), str(error))


def _has_drawing():
    try:
        doc.active()
    except RuntimeError:
        return False
    return True


def _rename(names):
    old = _pick_layout(names, 'Layout to rename')
    if old is None:
        return
    new = forms.ask_string('New name for %s' % old, old, _TITLE)
    if new is None:
        return
    new = new.strip()
    if not new:
        toast.warning('Nothing renamed', 'A layout needs a name.')
        return
    if new == old:
        toast.info('Nothing renamed', 'The name did not change.')
        return
    layouts.rename(old, new)
    toast.success('Renamed %s to %s' % (old, new))


def _reorder(names):
    if len(names) < 2:
        toast.info('Nothing to reorder', 'The drawing has one layout.')
        return
    items = [('%d. %s' % (i + 1, name), name) for i, name in enumerate(names)]
    name = forms.select_from_list(items, _TITLE, prompt='Layout to move',
                                  checked=_current_among(names))
    if name is None:
        return
    now = names.index(name) + 1
    position = forms.ask_number('New position for %s, from 1 to %d (now %d)'
                                % (name, len(names), now),
                                default=now, min_value=1, max_value=len(names), title=_TITLE)
    if position is None:
        return
    order = layouts.reorder(names, name, int(round(position)) - 1)
    if order == names:
        toast.info('Nothing moved', '%s is already at position %d.' % (name, now))
        return
    layouts.set_order(order)
    toast.success('Moved %s to position %d' % (name, order.index(name) + 1))


def _copy(names):
    source = _pick_layout(names, 'Layout to copy')
    if source is None:
        return
    wanted = forms.ask_string('Name for the copy of %s' % source,
                              layouts.unique_name(names, source), _TITLE)
    if wanted is None:
        return
    if not wanted.strip():
        toast.warning('Nothing copied', 'The copy needs a name.')
        return
    # Like AutoCAD's own Move or Copy, a name already in use gains a counter.
    name = layouts.copy(source, layouts.unique_name(names, wanted))
    toast.success('Copied %s to %s' % (source, name))


def _delete(names):
    doomed = forms.select_from_list(names, _TITLE, multiselect=True, prompt='Layouts to delete')
    if doomed is None:
        return
    if not doomed:
        toast.info('Nothing deleted', 'No layout was ticked.')
        return
    if len(doomed) == len(names):
        toast.warning('Nothing deleted', 'A drawing keeps at least one layout. Untick one and try again.')
        return
    if not forms.confirm('Delete %s? Everything drawn on %s goes with %s.'
                         % (_listing(doomed), 'it' if len(doomed) == 1 else 'them',
                            'it' if len(doomed) == 1 else 'them'), _TITLE):
        return
    if layouts.current().lower() in [n.lower() for n in doomed]:
        layouts.set_current('Model')
    deleted = []
    failures = []
    for name in doomed:
        try:
            layouts.delete(name)
            deleted.append(name)
        except Exception as error:
            failures.append('%s: %s' % (name, error))
    if failures:
        toast.warning('Deleted %d of %d layouts' % (len(deleted), len(doomed)), '; '.join(failures))
    else:
        toast.success('Deleted %s' % _count(len(deleted), 'layout', 'layouts'), _listing(deleted))


def _apply_page_setup(names):
    setups = layouts.page_setups(paper_only=True)
    if not setups:
        toast.info('No page setups',
                   'This drawing has no named paper space page setups. Make one with PAGESETUP first.')
        return
    setup = forms.select_from_list(setups, _TITLE, prompt='Page setup to apply')
    if setup is None:
        return
    targets = forms.select_from_list(names, _TITLE, multiselect=True,
                                     prompt='Layouts to apply %s to' % setup, checked=names)
    if targets is None:
        return
    if not targets:
        toast.info('Nothing changed', 'No layout was ticked.')
        return
    count = layouts.apply_page_setup(setup, targets)
    toast.success('Applied %s to %s' % (setup, _count(count, 'layout', 'layouts')))


def _pick_layout(names, prompt):
    """One layout, the current one picked to start with."""
    return forms.select_from_list(names, _TITLE, prompt=prompt, checked=_current_among(names))


def _current_among(names):
    current = layouts.current()
    return [current] if current in names else None


def _listing(names, limit=4):
    """'A1', 'A1 and A2', 'A1, A2 and A3', or the first few and how many more."""
    names = list(names)
    if len(names) > limit:
        return '%s and %d more' % (', '.join(names[:limit - 1]), len(names) - limit + 1)
    if len(names) == 1:
        return names[0]
    return '%s and %s' % (', '.join(names[:-1]), names[-1])


def _count(n, one, many):
    return '%d %s' % (n, one if n == 1 else many)


_ACTIONS = [
    ('Rename', _rename),
    ('Reorder', _reorder),
    ('Copy', _copy),
    ('Delete', _delete),
    ('Apply page setup', _apply_page_setup),
]


run()
