# -*- coding: utf-8 -*-
"""Applies a layer standard from a CSV: renames, merges and creates layers.

The CSV has the columns old,new[,color][,linetype], one row per mapping; old may
use * and ? wildcards and the first row that fits a layer wins. A layer is
renamed to new, or merged into it when a layer of that name already exists.
New names that no layer was renamed to can be created too, with the row's color
and linetype. Everything planned is listed with a tick box before anything
changes, so a row can be left out. The pure work is layers.parse_mapping and
layers.plan_mapping.
"""

__title__ = 'Layer Mapper'

import codecs

from pyacad import doc, forms, layers, script, toast

CSV_FILTER = 'CSV files (*.csv)|*.csv|Text files (*.txt)|*.txt|All files (*.*)|*.*'
SHOWN = 8           # names listed in a prompt before "and N more"


def has_drawing():
    try:
        doc.active()
        return True
    except RuntimeError:
        return False


def read_text(path):
    """The file's text: UTF-16 when it starts with that byte order mark (Excel's
    "Unicode text"), UTF-8 when it decodes, else the Windows code page Excel
    saves plain CSV in."""
    with open(path, 'rb') as f:
        data = f.read()
    if data.startswith(codecs.BOM_UTF16_LE) or data.startswith(codecs.BOM_UTF16_BE):
        return data.decode('utf-16')
    for encoding in ('utf-8', 'cp1252'):
        try:
            return data.decode(encoding)
        except (UnicodeDecodeError, LookupError):
            continue
    return data.decode('latin-1')


def listed(items, more='and %d more'):
    """Up to SHOWN items, one per line, then a count of the rest."""
    lines = ['  ' + str(item) for item in items[:SHOWN]]
    if len(items) > SHOWN:
        lines.append('  ' + more % (len(items) - SHOWN))
    return '\n'.join(lines)


def words(step):
    """A plan step in words without its verb: 'MECH (color 4)', '0: reason'."""
    return layers.describe_action(step).split(' ', 1)[1]


def apply(actions):
    """Runs the steps in plan order; returns (counts, objects moved, failures)."""
    counts = {'rename': 0, 'merge': 0, 'create': 0}
    moved = 0
    failures = []
    for step in actions:
        kind = step[0]
        try:
            if kind == 'rename':
                layers.rename(step[1], step[2])
            elif kind == 'merge':
                moved += layers.merge(step[1], step[2])
            elif kind == 'create':
                layers.create(step[1], step[2] or 7, step[3] or 'Continuous')
            else:
                continue
            counts[kind] += 1
        except Exception as error:
            failures.append('%s: %s' % (layers.describe_action(step), error))
    return counts, moved, failures


def summary(counts, moved):
    parts = []
    if counts['rename']:
        parts.append('%d renamed' % counts['rename'])
    if counts['merge']:
        parts.append('%d merged (%d object%s moved)' % (counts['merge'], moved,
                                                         '' if moved == 1 else 's'))
    if counts['create']:
        parts.append('%d created' % counts['create'])
    return ', '.join(parts) or 'nothing changed'


def run():
    if not has_drawing():
        toast.warning('Open a drawing first', 'Layer Mapper changes the active drawing')
        return

    path = forms.open_file(CSV_FILTER, 'Pick a layer standard (old,new,color,linetype)')
    if not path:
        return
    try:
        mapping = layers.parse_mapping(read_text(path))
    except (IOError, OSError) as error:
        toast.error('Could not read the CSV', str(error))
        return
    except ValueError as error:
        toast.error('The CSV has a problem', str(error))
        return
    if not mapping:
        toast.warning('The CSV has no mapping rows', 'Expected columns old,new[,color][,linetype]')
        return

    plan = layers.plan_mapping(layers.names(), mapping)
    changes = [step for step in plan if step[0] in ('rename', 'merge')]
    creates = [step for step in plan if step[0] == 'create']
    skips = [step for step in plan if step[0] == 'skip']

    if creates and not forms.confirm(
            '%d layer%s in the standard %s not in this drawing:\n\n%s\n\nCreate them too?'
            % (len(creates), '' if len(creates) == 1 else 's',
               'is' if len(creates) == 1 else 'are',
               listed([words(s) for s in creates])),
            'Layer Mapper'):
        creates = []

    steps = changes + creates
    if not steps:
        if skips:
            forms.alert('Nothing to change. These layers were left alone:\n\n%s'
                        % listed([words(s) for s in skips]),
                        'Layer Mapper')
        else:
            toast.info('Nothing to change', 'The layers already follow the standard')
        return

    prompt = 'Untick anything that should not change.'
    if skips:
        prompt += ' %d layer%s left alone: %s.' % (
            len(skips), '' if len(skips) == 1 else 's',
            '; '.join(words(s) for s in skips[:3])
            + ('; ...' if len(skips) > 3 else ''))
    picked = forms.select_from_list([(layers.describe_action(s), s) for s in steps],
                                    title='Layer Mapper: %d change%s'
                                          % (len(steps), '' if len(steps) == 1 else 's'),
                                    multiselect=True, prompt=prompt, checked=steps)
    if not picked:
        return
    # The picker hands back what was ticked; run it in plan order, since a merge
    # may target a name an earlier rename made.
    picked = [step for step in steps if step in picked]

    counts, moved, failures = apply(picked)
    for failure in failures:
        script.get_logger().warning(failure)
    if failures:
        toast.warning('Layer standard applied with %d problem%s'
                      % (len(failures), '' if len(failures) == 1 else 's'),
                      '%s. %s' % (summary(counts, moved), failures[0]))
    else:
        toast.success('Layer standard applied', summary(counts, moved))


if '__commandpath__' in globals():
    run()
