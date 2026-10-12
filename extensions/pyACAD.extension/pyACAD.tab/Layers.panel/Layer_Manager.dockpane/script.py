# -*- coding: utf-8 -*-
"""The Layer Manager palette: the active drawing's layers, filtered, with actions.

The list reads the layer table in one transaction (layers.snapshot) and reads it
again whenever the mouse comes back into the palette, so it follows changes made
elsewhere without a timer, and re-draws only when something changed so the
selection and scroll stay put. Every button acts on the selected rows through
pyacad.layers under a document lock, because a palette click runs outside any
command, then refreshes the list. A failure toasts its message.

This runs once, when the palette is built; the handlers it wires fire later, so
they act through `pane`, never through whichever command ran last.
"""

__title__ = 'Layer Manager'

import clr

clr.AddReference('PresentationCore')
clr.AddReference('PresentationFramework')
clr.AddReference('WindowsBase')
clr.AddReference('PyAcad.Runtime')

from System import String
from System.Collections.Generic import List
from System.Windows import Setter, Style
from System.Windows.Controls import Control, GridViewColumnHeader, ItemsControl
from System.Windows.Documents import TextElement
from PyAcad.Runtime.Forms import DesignSystem

from pyacad import doc, forms, layers, script, toast

pane = __pane__
log = script.get_logger()

root = pane.Find('Root')
filter_box = pane.Find('Filter')
layer_list = pane.Find('LayerList')
status = pane.Find('Status')
ACTIONS = ('Isolate', 'FreezeThaw', 'OnOff', 'LockUnlock', 'SetCurrent', 'Merge', 'Delete')
buttons = dict((name, pane.Find(name)) for name in ACTIONS)

# What the list shows: the last layer read, the current layer, or why there is none.
view = {'snapshot': {}, 'current': '', 'error': None}


# ---- reading and showing ------------------------------------------------------

def refresh(quiet=False):
    """Reads the active drawing's layers and re-draws the list if they changed.

    quiet (mouse-enter and visibility refreshes) keeps the current list when the
    read fails for a passing reason, such as a command holding the drawing.
    """
    try:
        doc.active()
    except RuntimeError as error:
        update({}, '', str(error), force=not quiet)
        return
    try:
        with doc.lock():
            snapshot = layers.snapshot()
            current = layers.current()
    except Exception as error:
        if not quiet:
            log.warning('could not read the layers: %s' % error)
            update({}, '', 'Could not read the layers: %s' % error, force=True)
        return
    update(snapshot, current, None, force=not quiet)


def update(snapshot, current, error, force=False):
    same = (snapshot == view['snapshot'] and current == view['current']
            and error == view['error'])
    if same and not force:
        return
    view.update(snapshot=snapshot, current=current, error=error)
    show()


def show():
    """Fills the list from the last read, through the filter box, keeping the selection."""
    keep = set(selected())
    snapshot = view['snapshot']
    names = layers.filter_names(sorted(snapshot, key=lambda n: n.lower()), filter_box.Text)
    rows = List[List[String]]()
    for name in names:
        rows.Add(row_for(name, snapshot[name]))
    layer_list.ItemsSource = rows
    for row in rows:
        if row[0] in keep:
            layer_list.SelectedItems.Add(row)
    on_selection()


def row_for(name, state):
    """The list row: [0] name, [1] current mark, [2] on, [3] frozen, [4] locked, [5] color."""
    row = List[String]()
    cells = (name,
             u'\u2713' if name.lower() == view['current'].lower() else '',
             'No' if state.get('is_off') else 'Yes',
             'Yes' if state.get('is_frozen') else 'No',
             'Yes' if state.get('is_locked') else 'No',
             str(state.get('color', '')))
    for cell in cells:
        row.Add(cell)
    return row


def selected():
    return [str(row[0]) for row in layer_list.SelectedItems]


def on_selection(sender=None, args=None):
    """Status line and which buttons apply to the selection."""
    count = layer_list.SelectedItems.Count
    for name, button in buttons.items():
        button.IsEnabled = count == 1 if name == 'SetCurrent' else count > 0
    if view['error']:
        status.Text = view['error']
        return
    total = len(view['snapshot'])
    shown = layer_list.Items.Count
    parts = ['%d layers' % total if shown == total else '%d of %d layers' % (shown, total),
             'current: %s' % view['current']]
    if count:
        parts.append('%d selected' % count)
    status.Text = u'  \u00b7  '.join(parts)


def counted(n):
    n = n if isinstance(n, int) else len(n)
    return '%d layer%s' % (n, '' if n == 1 else 's')


# ---- actions ------------------------------------------------------------------

def action(label, work):
    """A click handler running work(selected names), toasting any failure, then
    re-reading the layers."""
    def handle(sender, args):
        names = selected()
        if not names:
            toast.info('Select one or more layers first')
            return
        try:
            work(names)
        except Exception as error:
            log.error('%s failed: %s' % (label, error))
            toast.error('%s failed' % label, str(error))
        refresh()
    return handle


def all_set(names, flag):
    """True when every named layer has flag set, read fresh from the drawing."""
    states = layers.snapshot()
    return all(states.get(name, {}).get(flag) for name in names)


def isolate(names):
    with doc.lock():
        frozen = layers.isolate(names)
    toast.success('Isolated %s' % counted(names), '%s frozen' % counted(frozen))


def freeze_thaw(names):
    with doc.lock():
        if all_set(names, 'is_frozen'):
            verb, changed = 'Thawed', layers.thaw(names)
        else:
            verb, changed = 'Froze', layers.freeze(names)
    detail = None
    if verb == 'Froze' and view['current'].lower() in [n.lower() for n in names]:
        detail = 'The current layer cannot be frozen, so it stays thawed'
    toast.success('%s %s' % (verb, counted(changed)), detail)


def on_off(names):
    with doc.lock():
        if all_set(names, 'is_off'):
            verb, changed = 'Turned on', layers.turn_on(names)
        else:
            verb, changed = 'Turned off', layers.turn_off(names)
    toast.success('%s %s' % (verb, counted(changed)))


def lock_unlock(names):
    with doc.lock():
        if all_set(names, 'is_locked'):
            verb, changed = 'Unlocked', layers.unlock(names)
        else:
            verb, changed = 'Locked', layers.lock(names)
    toast.success('%s %s' % (verb, counted(changed)))


def set_current(names):
    if len(names) != 1:
        toast.info('Select just one layer to make current')
        return
    with doc.lock():
        layers.set_current(names[0])
    toast.success('%s is now the current layer' % names[0])


def merge(names):
    target = forms.ask_string(
        'Merge %s into which layer? Everything on them moves there, then they are deleted.'
        % counted(names), '', 'Merge layers')
    target = (target or '').strip()
    if not target:
        return
    sources = [n for n in names if n.lower() != target.lower()]
    if not sources:
        toast.info('Nothing to merge', 'The only layer selected is the target')
        return
    with doc.lock():
        exists = layers.exists(target)
    if not exists and not forms.confirm(
            'There is no layer named %s. Create it and merge into it?' % target, 'Merge layers'):
        return
    with doc.lock():
        if not exists:
            layers.create(target)
        moved = layers.merge(sources, target)
    toast.success('Merged %s into %s' % (counted(sources), target),
                  '%d object%s moved' % (moved, '' if moved == 1 else 's'))


def delete(names):
    if not forms.confirm('Delete %s?\n\nThe current layer, layer 0, Defpoints, xref layers '
                         'and layers still in use are kept.' % counted(names), 'Delete layers'):
        return
    deleted, kept = 0, []
    with doc.lock():
        for name in names:
            try:
                layers.delete(name)
                deleted += 1
            except ValueError as error:
                kept.append(str(error))
    for reason in kept:
        log.info('kept: %s' % reason)
    if not kept:
        toast.success('Deleted %s' % counted(deleted))
    elif deleted:
        toast.warning('Deleted %s, kept %d' % (counted(deleted), len(kept)), kept[0])
    else:
        toast.warning('No layers deleted', kept[0])


def on_double_click(sender, args):
    """Double-clicking a row (not the header) makes that layer current."""
    if ItemsControl.ContainerFromElement(layer_list, args.OriginalSource) is None:
        return
    action('Set current', set_current)(sender, args)


# ---- build --------------------------------------------------------------------

def apply_theme():
    """Paper and ink from the host's light or dark theme. The column headers keep
    the default light chrome, so their text stays dark in both."""
    tokens = DesignSystem.Tokens.Current
    surface = DesignSystem.Brush(tokens.Surface)
    ink = DesignSystem.Brush(tokens.Ink)
    line = DesignSystem.Brush(tokens.Line)
    root.Background = DesignSystem.Brush(tokens.Paper)
    root.SetValue(TextElement.ForegroundProperty, ink)
    for box in (filter_box, layer_list):
        box.Background = surface
        box.Foreground = ink
        box.BorderBrush = line
    filter_box.CaretBrush = ink
    header = Style(clr.GetClrType(GridViewColumnHeader))
    header.Setters.Add(Setter(Control.ForegroundProperty,
                              DesignSystem.Brush(DesignSystem.Tokens.For(False).Ink)))
    layer_list.View.ColumnHeaderContainerStyle = header


def quietly(handler):
    """A handler that logs what it raises: an exception escaping a WPF event
    inside AutoCAD is far worse than a list that did not update."""
    def handle(sender, args):
        try:
            handler(sender, args)
        except Exception as error:
            log.error('Layer Manager: %s' % error)
    return handle


try:
    apply_theme()
except Exception as error:                      # cosmetic: the palette still works
    log.warning('could not theme the Layer Manager: %s' % error)

pane.Find('Refresh').Click += quietly(lambda sender, args: refresh())
filter_box.TextChanged += quietly(lambda sender, args: show())
layer_list.SelectionChanged += quietly(on_selection)
layer_list.MouseDoubleClick += quietly(on_double_click)
root.MouseEnter += quietly(lambda sender, args: refresh(quiet=True))
root.IsVisibleChanged += quietly(
    lambda sender, args: refresh(quiet=True) if root.IsVisible else None)

for name, label, work in (('Isolate', 'Isolate', isolate),
                          ('FreezeThaw', 'Freeze/Thaw', freeze_thaw),
                          ('OnOff', 'On/Off', on_off),
                          ('LockUnlock', 'Lock/Unlock', lock_unlock),
                          ('SetCurrent', 'Set current', set_current),
                          ('Merge', 'Merge', merge),
                          ('Delete', 'Delete', delete)):
    buttons[name].Click += quietly(action(label, work))

refresh()
