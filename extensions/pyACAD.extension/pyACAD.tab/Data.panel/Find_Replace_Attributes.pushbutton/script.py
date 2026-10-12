# -*- coding: utf-8 -*-
"""Find and replace text inside block attribute values.

Asks what to find and what to put instead (both remembered for next time),
then where: the whole drawing, the selected blocks, or blocks picked now. Then
which attribute (every tag, or one) and whether case must match. The text is
plain, never a pattern. Every change is one Undo; blocks on locked layers are
left alone and reported.
"""

from pyacad import blocks, forms, script, sel, toast

__title__ = 'Find and Replace Attributes'

TITLE = 'Find and Replace Attributes'
DEFAULTS = {'find': '', 'replace': '', 'match_case': False}
WHOLE = 'Whole drawing'
PICK = 'Pick blocks'
ALL_TAGS = 'Every tag'
IGNORE_CASE = 'Ignore case'
MATCH_CASE = 'Match case'


def plural(count, word):
    return '%d %s%s' % (count, word, '' if count == 1 else 's')


def choose_refs():
    """The block reference snapshots to search, or None when cancelled."""
    picked = sel.current()
    options = [WHOLE]
    selected = None
    if picked:
        selected = 'Selection (%s)' % plural(len(picked), 'object')
        options.insert(0, selected)
    options.append(PICK)
    where = forms.ask_options('Replace in which block references?', options, TITLE)
    if where is None:
        return None
    if where == WHOLE:
        return blocks.snapshots()
    if where == PICK:
        picked = sel.pick_many('Select block references: ')
        if not picked:
            return None
    return blocks.snapshots(picked)


def run():
    if script.get_document_path() is None:
        toast.warning('No drawing is open', 'Open a drawing, then run %s' % TITLE)
        return

    saved = script.get_config(DEFAULTS)
    find = forms.ask_string('Find this text in attribute values:', saved['find'], TITLE)
    if find is None:
        return
    if not find:
        toast.info('Nothing to find', 'Type the text to look for')
        return
    replace = forms.ask_string('Replace "%s" with (leave empty to remove it):' % find,
                               saved['replace'], TITLE)
    if replace is None:
        return

    refs = choose_refs()
    if refs is None:
        return
    tags = blocks.attribute_tags(refs)
    if not tags:
        toast.info('No attributes to search', 'None of those block references has attributes')
        return

    tag = forms.select_from_list([(ALL_TAGS, '')] + tags, title=TITLE,
                                 prompt='Replace in which attribute?')
    if tag is None:
        return
    case = forms.ask_options('Match case?', [IGNORE_CASE, MATCH_CASE] if not saved['match_case']
                             else [MATCH_CASE, IGNORE_CASE], TITLE)
    if case is None:
        return
    match_case = case == MATCH_CASE
    script.save_config({'find': find, 'replace': replace, 'match_case': match_case})

    found = blocks.replace_in_attributes(refs, find, replace, case_sensitive=match_case,
                                         tag=tag or None)
    if not found:
        toast.info('No matches', 'No attribute value contains "%s"' % find)
        return

    result = blocks.apply_changes(dict((handle, {'attributes': values})
                                       for handle, values in found.items()))
    detail = 'in %s' % plural(result['updated'], 'block reference')
    if result['failed']:
        detail += '; %s could not be changed (locked layer?)' % plural(len(result['failed']), 'block')
        toast.warning('Replaced %s' % plural(result['values'], 'value'), detail)
    else:
        toast.success('Replaced %s' % plural(result['values'], 'value'), detail)


if '__commandpath__' in globals():
    run()
