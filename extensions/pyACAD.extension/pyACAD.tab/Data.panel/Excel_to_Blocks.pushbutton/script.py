# -*- coding: utf-8 -*-
"""Writes a workbook from Blocks to Excel back into the drawing.

Rows are matched to block references by Handle. Only attribute values and
Layer go back: a changed or cleared attribute value is written, a filled-in
Layer that differs moves the block to that layer (it must exist). Position,
rotation and scale columns are read-only. Rows whose block no longer exists are
skipped and listed, and the whole update is one Undo.
"""

from pyacad import blocks, forms, script, toast, xl

__title__ = 'Excel to Blocks'

TITLE = 'Excel to Blocks'
XLSX = 'Excel workbook (*.xlsx)|*.xlsx'
SHOWN = 15                      # handles listed in a dialog before "and N more"


def plural(count, word):
    return '%d %s%s' % (count, word, '' if count == 1 else 's')


def read_sheet(path):
    """The rows of the workbook's Blocks sheet (else its first sheet), or None
    after saying why it could not be read."""
    try:
        names = xl.sheets(path)
        return xl.read(path, sheet='Blocks' if 'Blocks' in names else None)
    except Exception as error:      # not a workbook, locked, damaged: say so either way
        toast.error('Could not read the workbook', str(error))
        return None


def listing(handles):
    shown = ', '.join(handles[:SHOWN])
    if len(handles) > SHOWN:
        shown += ' and %d more' % (len(handles) - SHOWN)
    return shown


def describe(changes):
    """'12 block references: 30 attribute values, 2 layers'."""
    values = sum(len(change.get('attributes', {})) for change in changes.values())
    moves = sum(1 for change in changes.values() if 'layer' in change)
    parts = []
    if values:
        parts.append(plural(values, 'attribute value'))
    if moves:
        parts.append(plural(moves, 'layer change'))
    return '%s: %s' % (plural(len(changes), 'block reference'), ', '.join(parts))


def report(skipped, failed):
    """The dialog text for rows that did not go in; None when they all did."""
    lines = []
    if skipped:
        lines.append('Skipped %s whose block is no longer in this drawing:\n%s'
                     % (plural(len(skipped), 'row'), listing(skipped)))
    if failed:
        lines.append('%s could not be updated:' % plural(len(failed), 'block'))
        for handle in sorted(failed)[:SHOWN]:
            lines.append('  %s: %s' % (handle, failed[handle]))
        if len(failed) > SHOWN:
            lines.append('  and %d more' % (len(failed) - SHOWN))
    return '\n\n'.join(lines) if lines else None


def full_report(skipped, failed):
    lines = ['Skipped (not in the drawing): %s' % h for h in skipped]
    lines += ['Failed %s: %s' % (h, failed[h]) for h in sorted(failed)]
    return '\n'.join(lines)


def run():
    if script.get_document_path() is None:
        toast.warning('No drawing is open', 'Open a drawing, then run %s' % TITLE)
        return

    path = forms.open_file(XLSX, 'Pick a workbook made by Blocks to Excel')
    if not path:
        return                                          # cancelled: say nothing
    sheet = read_sheet(path)
    if sheet is None:
        return

    try:
        changes, skipped = blocks.diff_rows(blocks.snapshots(), sheet)
    except ValueError as error:
        forms.alert(str(error), TITLE)
        return

    if not changes:
        text = report(skipped, {})
        if text:
            forms.alert('Nothing to update: every other row matches the drawing.\n\n' + text,
                        TITLE, copy=full_report(skipped, {}))
        else:
            toast.info('Nothing to update', 'The workbook matches the drawing')
        return

    if not forms.confirm('Update %s?\n\nUndo takes it back.' % describe(changes), TITLE):
        return

    result = blocks.apply_changes(changes)
    detail = plural(result['values'], 'attribute value') + ' written'
    if skipped:
        detail += ', %s skipped' % plural(len(skipped), 'row')
    if result['failed']:
        toast.warning('Updated %s' % plural(result['updated'], 'block reference'), detail)
    else:
        toast.success('Updated %s' % plural(result['updated'], 'block reference'), detail)

    text = report(skipped, result['failed'])
    if text:
        forms.alert(text, TITLE, copy=full_report(skipped, result['failed']))


if '__commandpath__' in globals():
    run()
