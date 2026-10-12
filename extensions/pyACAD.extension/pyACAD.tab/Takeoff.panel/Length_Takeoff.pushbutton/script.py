# -*- coding: utf-8 -*-
"""Run lengths per layer: duct, pipe and conduit takeoff in drawing units.

Totals the length of lines, arcs, polylines (lightweight, 2D and 3D) and
splines in the selection, or in all of model space when nothing is selected,
grouped by layer. Lengths are shown in the drawing's units (feet and inches for
imperial drawings). The totals can go to Excel or onto the clipboard.
"""

import os

from pyacad import ents, forms, script, sel, toast, units, xl

__title__ = 'Length Takeoff'

TITLE = 'Length Takeoff'
XLSX = 'Excel workbook (*.xlsx)|*.xlsx'
SHOWN = 20                      # layers listed in the dialog before "and N more"
EXPORT = 'Export to Excel'
COPY = 'Copy'
CLOSE = 'Close'


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


def unit_label(unit):
    """The short label of a unit name ('mm'), '' for Undefined."""
    for name, _, label in units.UNITS.values():
        if name.lower() == unit.lower():
            return label
    return ''


def summary(totals, unit, scope):
    total = sum(row['length'] for row in totals)
    count = sum(row['count'] for row in totals)
    lines = ['%s: %s in %s on %s.' % (scope, units.format_length(total, unit),
                                        plural(count, 'object'), plural(len(totals), 'layer')), '']
    for row in totals[:SHOWN]:
        lines.append('%s    %s  (%d)' % (row['layer'], units.format_length(row['length'], unit),
                                         row['count']))
    if len(totals) > SHOWN:
        lines.append('and %s more: export or copy for the full list' % plural(len(totals) - SHOWN, 'layer'))
    return '\n'.join(lines)


def table(totals, unit):
    """(headers, rows) for Excel and the clipboard, with a Total row."""
    label = unit_label(unit)
    headers = ['Layer', 'Objects', 'Length (%s)' % label if label else 'Length', 'Length']
    rows = [[row['layer'], row['count'], round(row['length'], 6),
             units.format_length(row['length'], unit)] for row in totals]
    total = sum(row['length'] for row in totals)
    rows.append(['Total', sum(row['count'] for row in totals), round(total, 6),
                 units.format_length(total, unit)])
    return headers, rows


def tab_text(headers, rows):
    return '\n'.join('\t'.join(str(cell) for cell in row) for row in [headers] + rows)


def run():
    drawing = script.get_document_path()
    if drawing is None:
        toast.warning('No drawing is open', 'Open a drawing, then run %s' % TITLE)
        return

    picked = sel.current()
    handles = picked if picked else list(ents.iter())
    scope = 'Selection' if picked else 'Model space'
    measured = ents.lengths(handles)
    totals = ents.length_by_layer(measured)
    if not totals:
        toast.info('Nothing to measure',
                   'No lines, arcs, polylines or splines in the %s' % scope.lower())
        return

    unit = units.current()
    headers, rows = table(totals, unit)
    choice = forms.ask_options(summary(totals, unit, scope), [EXPORT, COPY, CLOSE], TITLE)
    if choice == COPY:
        script.clipboard_copy(tab_text(headers, rows))
        toast.success('Takeoff copied', 'Paste it into Excel or a document')
    elif choice == EXPORT:
        path = forms.save_file(XLSX, default_path(drawing, TITLE), 'Export length takeoff')
        if path and write_workbook(path, headers, rows, 'Takeoff'):
            toast.success('Takeoff exported: %s' % plural(len(totals), 'layer'), path)

    unmeasured = sum(1 for item in measured if item['length'] is None)
    if unmeasured:
        toast.warning('%s could not be measured' % plural(unmeasured, 'object'),
                      'They are left out of the totals')


if '__commandpath__' in globals():
    run()
