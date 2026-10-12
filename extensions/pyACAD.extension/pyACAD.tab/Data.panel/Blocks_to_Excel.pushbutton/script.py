# -*- coding: utf-8 -*-
"""Exports block references to an Excel workbook, one row per reference.

The selected block references when there are any, else every one in model space
and the layouts. Columns: Handle, Block, Layer, Space, X, Y, Z, Rotation (degrees),
Scale, then one column per attribute tag found on any of them. Edit attribute
values or layers in Excel and Excel to Blocks writes them back, matched by Handle.
"""

import os

from pyacad import blocks, forms, script, sel, toast, xl

__title__ = 'Blocks to Excel'

TITLE = 'Blocks to Excel'
XLSX = 'Excel workbook (*.xlsx)|*.xlsx'


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
                       'Clear the selection to export the whole drawing')
        else:
            toast.info('No block references in this drawing')
        return

    path = forms.save_file(XLSX, default_path(drawing, 'Blocks'), 'Export blocks to Excel')
    if not path:
        return                                          # cancelled: say nothing

    headers, rows = blocks.export_rows(refs)
    if write_workbook(path, headers, rows, 'Blocks'):
        scope = 'selected ' if picked else ''
        toast.success('Exported %s' % plural(len(rows), scope + 'block reference'), path)


if '__commandpath__' in globals():
    run()
