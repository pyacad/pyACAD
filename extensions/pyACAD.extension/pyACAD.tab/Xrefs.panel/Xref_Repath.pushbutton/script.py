# -*- coding: utf-8 -*-
"""Change the saved paths of the drawing's xrefs in one go.

Four ways: relative to the drawing's folder, absolute, find and replace text in
every path, or point each xref at the file of the same name in one folder (only
where that file is really there; the rest are reported and left alone). Every
change is shown, old path and new, before anything is touched. Each repathed
xref reloads from its new path, except one that was unloaded, which keeps its
new path and stays unloaded. Nested xrefs follow their parent and are not
repathed here.
"""

import os

from pyacad import doc, forms, script, toast, xrefs

__title__ = 'Xref Repath'

TITLE = 'Xref Repath'
MODES = [
    ('Make relative', 'relative'),
    ('Make absolute', 'absolute'),
    ('Find and replace in paths', 'replace'),
    ('Point all at a folder', 'folder'),
]
LIMIT = 12          # changes shown in the preview; Copy carries them all


def run():
    if script.get_document_path() is None:
        toast.warning('Open a drawing first')
        return

    rows = sorted([x for x in xrefs.list() if not x['nested']],
                  key=lambda x: x['name'].lower())
    if not rows:
        toast.info('This drawing has no xrefs')
        return

    choice = forms.ask_options('How should the xref paths change?',
                               [label for label, mode in MODES], title=TITLE)
    if choice is None:
        return
    mode = dict(MODES)[choice]

    drawing = doc.path()
    host_dir = None if drawing == 'untitled' else os.path.dirname(drawing)
    if mode in ('relative', 'absolute') and host_dir is None:
        toast.warning('Save the drawing first', 'Relative paths start from its folder')
        return

    options = _ask(mode, host_dir)
    if options is None:
        return
    plan = xrefs.plan_repath(rows, mode, host_dir=host_dir, **options)

    full = xrefs.format_plan(rows, plan)
    forms.alert(xrefs.format_plan(rows, plan, limit=LIMIT), TITLE, copy=full)
    changes = [(row['name'], new) for row, (old, new, note) in zip(rows, plan) if not note]
    if not changes:
        return                      # the preview said so and why
    if not forms.confirm('Repath %s as shown?' % _what(changes), TITLE):
        return

    unloaded = set(row['name'] for row in rows if row['status'] == 'unloaded')
    watched = _watched(rows, mode)
    with doc.lock():
        results = [_repath(name, new, name not in unloaded) for name, new in changes]
        # AutoCAD takes a path it cannot resolve without complaint; the status says.
        results = xrefs.check_loaded(
            results, [row for row in xrefs.list() if row['name'] in watched])
    _report(results, len(rows) - len(changes))


def _watched(rows, mode):
    """Names whose status after the repath says whether it worked: any that
    were loaded (a repath must not break them) and, in the two modes meant to
    fix paths, any that were missing. A missing xref made relative is still
    missing, and that is not this tool's failure."""
    fixing = mode in ('replace', 'folder')
    return set(row['name'] for row in rows
               if row['status'] == 'loaded' or (fixing and row['status'] != 'unloaded'))


def _ask(mode, host_dir):
    """plan_repath's mode options from the person; None when they cancel."""
    if mode == 'replace':
        find = forms.ask_string('Text to find in every xref path (case does not matter):',
                                title=TITLE)
        if find is None:
            return None
        if not find:
            toast.warning('Nothing to find', 'Type the text to look for')
            return None
        replace = forms.ask_string('Replace "%s" with:' % find, title=TITLE)
        if replace is None:
            return None
        return {'find': find, 'replace': replace}
    if mode == 'folder':
        folder = forms.pick_folder('Folder holding the xref files', initial=host_dir)
        if not folder:
            return None
        return {'folder': folder, 'exists': os.path.isfile}
    return {}


def _repath(name, new_path, reload):
    """(name, error) for one xref; error is None when it worked."""
    try:
        xrefs.repath(name, new_path, reload=reload)
    except Exception as error:
        return name, str(error) or error.__class__.__name__
    return name, None


def _report(results, kept):
    head = xrefs.headline(results, 'Repathed')
    detail = '%d left as they were' % kept if kept else None
    failed = len([1 for name, error in results if error])
    if not failed:
        toast.success(head, detail)
        return
    if failed == len(results):
        toast.error(head, detail)
    else:
        toast.warning(head, detail)
    forms.alert(xrefs.summarize(results, 'Repathed', limit=LIMIT), TITLE,
                copy=xrefs.summarize(results, 'Repathed'))


def _what(changes):
    """'Site' for one change, '3 xrefs' for more."""
    return changes[0][0] if len(changes) == 1 else _xrefs(len(changes))


def _xrefs(count):
    return '1 xref' if count == 1 else '%d xrefs' % count


if '__commandpath__' in globals():
    run()
