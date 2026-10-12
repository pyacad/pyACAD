# -*- coding: utf-8 -*-
"""Equipment tag audit: duplicate, blank and missing values of one attribute.

Checks the selected block references, or every one in model space and the
layouts. Pick the attribute (TAG by default, from every tag those blocks
carry). Values are compared trimmed and ignoring case, so 'AHU-1' and 'ahu-1 '
count as the same tag. The findings can be copied, exported to Excel, or the
offending blocks selected in the drawing.
"""

import os

from pyacad import blocks, forms, script, sel, toast, xl

__title__ = 'Tag Audit'

TITLE = 'Tag Audit'
XLSX = 'Excel workbook (*.xlsx)|*.xlsx'
SHOWN = 8                       # duplicate values listed before "and N more"
SELECT = 'Select them'
EXPORT = 'Export to Excel'
COPY = 'Copy report'
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


def by_block(handles, names, shown=5):
    """'Door (12), North (1)': how many of the handles each block name has,
    the most common first."""
    counts = {}
    for handle in handles:
        counts[names[handle]] = counts.get(names[handle], 0) + 1
    order = sorted(counts, key=lambda name: (-counts[name], name.lower()))
    text = ', '.join('%s (%d)' % (name, counts[name]) for name in order[:shown])
    if len(order) > shown:
        text += ' and %s' % plural(len(order) - shown, 'other block')
    return text


def summary(audit, tag, refs, scope, names):
    lines = ['%s across %s %s:' % (tag, plural(len(refs), 'block reference'), scope), '']
    duplicates = audit['duplicates']
    if duplicates:
        shared = sum(len(handles) for handles in duplicates.values())
        lines.append('%s used more than once, by %s:'
                     % (plural(len(duplicates), 'value'), plural(shared, 'block')))
        order = sorted(duplicates, key=lambda value: value.lower())
        for value in order[:SHOWN]:
            lines.append('    %s  x%d' % (value, len(duplicates[value])))
        if len(order) > SHOWN:
            lines.append('    and %d more' % (len(order) - SHOWN))
    else:
        lines.append('No value is used twice.')
    if audit['blanks']:
        lines.append('%s with a blank %s: %s' % (plural(len(audit['blanks']), 'block'), tag,
                                                 by_block(audit['blanks'], names)))
    if audit['missing']:
        lines.append('%s without a %s attribute: %s' % (plural(len(audit['missing']), 'block'),
                                                       tag, by_block(audit['missing'], names)))
    return '\n'.join(lines)


def findings(audit, by_handle):
    """(headers, rows) of every offending block, for Excel and the clipboard."""
    headers = ['Issue', 'Value', 'Handle', 'Block', 'Layer', 'Space']
    rows = []

    def add(issue, value, handle):
        ref = by_handle[handle]
        rows.append([issue, value, handle, ref.get('name', ''), ref.get('layer', ''),
                     ref.get('space', '')])

    for value in sorted(audit['duplicates'], key=lambda v: v.lower()):
        for handle in audit['duplicates'][value]:
            add('Duplicate', value, handle)
    for handle in audit['blanks']:
        add('Blank', '', handle)
    for handle in audit['missing']:
        add('Missing', '', handle)
    return headers, rows


def offenders(audit):
    """Duplicates and blanks: the blocks whose tag needs fixing. Blocks without
    the attribute are often other kinds of block, so they are not selected."""
    handles = []
    for value in audit['duplicates']:
        handles.extend(audit['duplicates'][value])
    return handles + audit['blanks']


def run():
    drawing = script.get_document_path()
    if drawing is None:
        toast.warning('No drawing is open', 'Open a drawing, then run %s' % TITLE)
        return

    picked = sel.current()
    refs = blocks.snapshots(picked if picked else None)
    scope = 'in the selection' if picked else 'in the drawing'
    if not refs:
        toast.info('No block references %s' % scope)
        return
    tags = blocks.attribute_tags(refs)
    if not tags:
        toast.info('Nothing to audit', 'None of the block references %s has attributes' % scope)
        return

    default = [t for t in tags if t.upper() == 'TAG'][:1]
    tag = forms.select_from_list(tags, title=TITLE, prompt='Audit which attribute?',
                                 checked=default or None)
    if not tag:
        return
    audit = blocks.tag_audit(refs, tag)
    if not audit['duplicates'] and not audit['blanks'] and not audit['missing']:
        toast.success('Every %s is filled in and unique' % tag,
                      '%s checked %s' % (plural(len(refs), 'block reference'), scope))
        return

    names = dict((ref['handle'], ref.get('name', '')) for ref in refs)
    by_handle = dict((ref['handle'], ref) for ref in refs)
    headers, rows = findings(audit, by_handle)
    to_fix = offenders(audit)
    options = ([SELECT] if to_fix else []) + [EXPORT, COPY, CLOSE]
    choice = forms.ask_options(summary(audit, tag, refs, scope, names), options, TITLE)

    if choice == SELECT:
        sel.set_current(to_fix)
        toast.info('%s selected' % plural(len(to_fix), 'block'),
                   'Duplicate and blank %s values' % tag)
    elif choice == EXPORT:
        path = forms.save_file(XLSX, default_path(drawing, TITLE), 'Export tag audit')
        if path and write_workbook(path, headers, rows, TITLE):
            toast.success('Tag audit exported: %s' % plural(len(rows), 'finding'), path)
    elif choice == COPY:
        script.clipboard_copy('\n'.join('\t'.join(str(c) for c in row) for row in [headers] + rows))
        toast.success('Tag audit copied', plural(len(rows), 'finding'))


if '__commandpath__' in globals():
    run()
