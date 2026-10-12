# -*- coding: utf-8 -*-
"""Counts block references by name, or by name and layer, and places the result.

Counts the selected block references when there are any, else every one in model
space and the layouts (xrefs are not blocks here). The schedule goes into the
drawing as an AutoCAD table at a picked point, in the current table style, or
into an Excel workbook. Both end with a Total row.
"""

import os

from pyacad import blocks, forms, script, sel, toast, xl

__title__ = 'Block Schedule'

TITLE = 'Block Schedule'
XLSX = 'Excel workbook (*.xlsx)|*.xlsx'
BY_NAME = 'Block name'
BY_LAYER = 'Block name and layer'
TO_TABLE = 'Table in the drawing'
TO_EXCEL = 'Excel workbook'


def plural(count, word):
    return '%d %s%s' % (count, word, '' if count == 1 else 's')


def default_path(drawing, what):
    """'<drawing> - <what>.xlsx' beside the drawing; just the name when unsaved."""
    if not drawing or drawing == 'untitled':
        return 'Drawing - %s.xlsx' % what
    folder, name = os.path.split(drawing)
    return os.path.join(folder, '%s - %s.xlsx' % (os.path.splitext(name)[0], what))


def write_workbook(path, headers, rows, sheet):
    """Writes a fresh workbook; True when it worked. The Save dialog already
    asked before replacing a file, so an old one is removed rather than merged."""
    try:
        if os.path.exists(path):
            os.remove(path)
        xl.write(path, rows, headers=headers, sheet=sheet)
    except (IOError, OSError) as error:
        toast.error('Could not write the workbook', 'Is it open in Excel? %s' % error)
        return False
    return True


def run():
    drawing = script.get_document_path()
    if drawing is None:
        toast.warning('No drawing is open', 'Open a drawing, then run %s' % TITLE)
        return

    picked = sel.current()
    refs = blocks.snapshots(picked if picked else None)
    if not refs:
        if picked:
            toast.info('No block references in the selection',
                       'Clear the selection to count the whole drawing')
        else:
            toast.info('No block references in this drawing')
        return

    scope = 'the %s selected' % plural(len(refs), 'block reference') if picked \
        else 'all %s in the drawing' % plural(len(refs), 'block reference')
    grouping = forms.ask_options('Count %s by' % scope, [BY_NAME, BY_LAYER], TITLE)
    if grouping is None:
        return
    headers, rows = blocks.schedule_rows(refs, by_layer=(grouping == BY_LAYER))
    kinds = len(set(row[0] for row in rows))
    rows.append(['Total'] + [''] * (len(headers) - 2) + [len(refs)])

    target = forms.ask_options('Put the schedule in', [TO_TABLE, TO_EXCEL], TITLE)
    if target is None:
        return
    summary = '%s, %s' % (plural(kinds, 'block'), plural(len(refs), 'reference'))

    if target == TO_TABLE:
        point = sel.pick_point('Pick the top left corner of the schedule: ')
        if point is None:
            return
        blocks.insert_table(point, headers, rows, title=TITLE)
        toast.success('Block schedule placed', summary)
        return

    path = forms.save_file(XLSX, default_path(drawing, TITLE), 'Export block schedule')
    if path and write_workbook(path, headers, rows, 'Schedule'):
        toast.success('Block schedule exported: ' + summary, path)


if '__commandpath__' in globals():
    run()
