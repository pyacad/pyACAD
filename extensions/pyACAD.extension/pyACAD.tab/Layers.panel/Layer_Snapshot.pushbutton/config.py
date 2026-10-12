# -*- coding: utf-8 -*-
"""Shift+Click on Layer Snapshot: lists the layer states saved for this drawing.

Reads the same per-drawing 'slots' entry script.py writes; changes nothing.
"""

__title__ = 'Layer Snapshot'

from pyacad import doc, forms, layers, script, toast


def run():
    try:
        doc.active()
    except RuntimeError:
        toast.warning('Open a drawing first', 'Layer states are saved per drawing')
        return
    slots = script.load_data('slots', {}, per_document=True)
    choices = layers.snapshot_choices(slots if isinstance(slots, dict) else {})
    if not choices:
        toast.info('No layer states saved for this drawing', 'Click Layer Snapshot to save one')
        return
    text = '\n'.join(label for label, _ in choices)
    forms.alert('%d saved layer state%s, newest first:\n\n%s'
                % (len(choices), '' if len(choices) == 1 else 's', text),
                'Layer Snapshot', copy=text)


if '__commandpath__' in globals():
    run()
