# -*- coding: utf-8 -*-
"""Saves the drawing's layer state under a name, puts a saved one back, or deletes one.

A state is which layers are on, frozen and locked (layers.snapshot). States are
kept per drawing with script.store_data(per_document=True), as one 'slots' entry
mapping each name to {'saved': 'YYYY-MM-DD HH:MM', 'layers': snapshot}.
Restoring changes only those three flags and skips layers the drawing no longer
has. Shift+Click (config.py) lists the saved states.
"""

__title__ = 'Layer Snapshot'

import datetime

from pyacad import doc, forms, layers, script, toast

SLOTS = 'slots'
TITLE = 'Layer Snapshot'


def has_drawing():
    try:
        doc.active()
        return True
    except RuntimeError:
        return False


def plural(n, word):
    return '%d %s%s' % (n, word, '' if n == 1 else 's')


def load_slots():
    slots = script.load_data(SLOTS, {}, per_document=True)
    return slots if isinstance(slots, dict) else {}


def saved_layers(slots, name):
    slot = slots.get(name)
    saved = slot.get('layers') if isinstance(slot, dict) else None
    return saved if isinstance(saved, dict) else {}


def save(slots):
    now = datetime.datetime.now().strftime('%Y-%m-%d %H:%M')
    name = (forms.ask_string('Name this layer state', now, 'Save layer state') or '').strip()
    if not name:
        return
    same = [key for key in slots if key.lower() == name.lower()]
    if same and not forms.confirm('Replace the saved state %s?' % same[0], 'Save layer state'):
        return
    for key in same:
        del slots[key]
    snapshot = layers.snapshot()
    slots[name] = {'saved': now, 'layers': snapshot}
    script.store_data(SLOTS, slots, per_document=True)
    toast.success('Saved layer state %s' % name, plural(len(snapshot), 'layer'))


def restore(slots):
    if not slots:
        toast.info('No layer states saved for this drawing', 'Choose Save first')
        return
    name = forms.select_from_list(layers.snapshot_choices(slots), title='Restore layer state',
                                  prompt='Pick the state to put back')
    if name is None:
        return
    saved = saved_layers(slots, name)
    present = set(n.lower() for n in layers.names())
    missing = len([n for n in saved if n.lower() not in present])
    changed = layers.restore(saved)
    detail = plural(changed, 'layer') + ' changed'
    if missing:
        detail += '; %s no longer in the drawing' % plural(missing, 'saved layer')
    toast.success('Restored %s' % name, detail)


def delete(slots):
    if not slots:
        toast.info('No layer states saved for this drawing')
        return
    names = forms.select_from_list(layers.snapshot_choices(slots), title='Delete layer states',
                                   multiselect=True, prompt='Tick the states to delete')
    if not names:
        return
    if not forms.confirm('Delete %s?\n\n%s' % (plural(len(names), 'saved layer state'),
                                               '\n'.join(names)), 'Delete layer states'):
        return
    for name in names:
        slots.pop(name, None)
    script.store_data(SLOTS, slots, per_document=True)
    toast.success('Deleted %s' % plural(len(names), 'layer state'))


def run():
    if not has_drawing():
        toast.warning('Open a drawing first', 'Layer states are saved per drawing')
        return
    choice = forms.ask_options('Save the layers as they are now, or bring back a saved state?',
                               ['Save', 'Restore', 'Delete'], TITLE)
    if choice is None:
        return
    try:
        {'Save': save, 'Restore': restore, 'Delete': delete}[choice](load_slots())
    except Exception as error:
        script.get_logger().error('%s failed: %s' % (choice, error))
        toast.error('Layer Snapshot: %s failed' % choice.lower(), str(error))


if '__commandpath__' in globals():
    run()
