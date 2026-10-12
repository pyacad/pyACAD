# -*- coding: utf-8 -*-
"""Reload, unload, detach or bind several of the drawing's xrefs at once.

Lists every top-level xref with its status and saved path, asks what to do
with the ones picked, and does it to each in turn, so one that fails does not
stop the rest. Detach and bind ask first: both take the xref out of the
drawing's control and only Undo brings it back. Nested xrefs are not listed,
since they follow the xref that holds them.
"""

from pyacad import doc, forms, script, toast, xrefs

__title__ = 'Xref Manager'

TITLE = 'Xref Manager'
ACTIONS = ['Reload', 'Unload', 'Detach', 'Bind']
DONE = {'Reload': 'Reloaded', 'Unload': 'Unloaded', 'Detach': 'Detached', 'Bind': 'Bound'}
LIMIT = 15          # failures shown in the alert; Copy carries them all


def run():
    if script.get_document_path() is None:
        toast.warning('Open a drawing first')
        return

    rows = sorted([x for x in xrefs.list() if not x['nested']],
                  key=lambda x: x['name'].lower())
    if not rows:
        toast.info('This drawing has no xrefs')
        return

    labels = xrefs.describe(rows)
    picked = forms.select_from_list(
        [(label, row['name']) for label, row in zip(labels, rows)],
        title=TITLE, multiselect=True, prompt='Pick the xrefs to work on')
    if not picked:
        return

    action = forms.ask_options('What should happen to %s?' % _what(picked),
                               ACTIONS, title=TITLE)
    if action is None:
        return

    insert = False
    if action == 'Bind':
        how = forms.ask_options(
            "Bind keeps each xref's layers and styles apart, renamed like "
            "Site$0$Walls. Insert merges them into the drawing's own, as INSERT does.",
            ['Bind', 'Insert'], title=TITLE)
        if how is None:
            return
        insert = how == 'Insert'

    if action in ('Detach', 'Bind') and not forms.confirm(_warning(action, insert, picked), TITLE):
        return

    status = dict((row['name'], row['status']) for row in rows)
    with doc.lock():
        results = [_apply(action, name, insert, status.get(name)) for name in picked]
        if action == 'Reload':
            # AutoCAD reloads a missing file without complaint; the status says.
            results = xrefs.check_loaded(results, xrefs.list())
    _report(results, DONE[action])


def _apply(action, name, insert, status):
    """(name, error) for one xref; error is None when it worked."""
    if action == 'Bind' and status != 'loaded':
        return name, '%s; only a loaded xref can be bound' % xrefs.status_label(status)
    try:
        if action == 'Reload':
            xrefs.reload([name])
        elif action == 'Unload':
            xrefs.unload([name])
        elif action == 'Detach':
            xrefs.detach(name)
        else:
            xrefs.bind([name], insert=insert)
    except Exception as error:
        return name, str(error) or error.__class__.__name__
    return name, None


def _report(results, verb):
    head = xrefs.headline(results, verb)
    failed = len([1 for name, error in results if error])
    if not failed:
        toast.success(head)
        return
    if failed == len(results):
        toast.error(head)
    else:
        toast.warning(head, '%d not done' % failed)
    forms.alert(xrefs.summarize(results, verb, limit=LIMIT), TITLE,
                copy=xrefs.summarize(results, verb))


def _warning(action, insert, picked):
    if action == 'Detach':
        return ('Detach %s? It leaves the drawing with every reference to it.'
                if len(picked) == 1 else
                'Detach %s? They leave the drawing with every reference to them.') % _what(picked)
    how = ' as inserted blocks' if insert else ''
    return ('Bind %s into the drawing%s? It stops following its file.'
            if len(picked) == 1 else
            'Bind %s into the drawing%s? They stop following their files.') % (_what(picked), how)


def _what(names):
    """'Site' for one xref, '3 xrefs' for more."""
    return names[0] if len(names) == 1 else '%d xrefs' % len(names)


if '__commandpath__' in globals():
    run()
