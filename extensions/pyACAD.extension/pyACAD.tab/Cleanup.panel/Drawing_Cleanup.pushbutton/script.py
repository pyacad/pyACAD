# -*- coding: utf-8 -*-
"""Drawing hygiene in one run: purge, audit, zero-length and duplicate
geometry, ByLayer colour if asked, zoom extents.

A checklist picks the steps and remembers the last choice (Shift+Click
forgets it). Steps that erase something are confirmed first. Every edit this
tool makes itself happens inside its own command, so Undo takes it back.
AUDIT and ZOOM are queued as commands of their own and run once the tool
finishes, so AUDIT's findings are on the command line rather than in the
report. Entities on locked layers are never touched.
"""

from pyacad import audit, doc, forms, script, toast

__title__ = 'Drawing Cleanup'

TITLE = 'Drawing Cleanup'
DEFAULTS = {'steps': None}          # None: nothing remembered yet, so tick the defaults

WHAT_IT_ERASES = {
    'purge': 'purge unused blocks, layers, linetypes and styles',
    'zero_length': 'delete zero-length lines and polylines',
    'duplicates': 'delete duplicate entities, keeping one of each',
}


def has_drawing():
    try:
        doc.active()
    except RuntimeError:
        return False
    return True


def choose_steps():
    """The ticked steps in run order, or None when the checklist was cancelled."""
    saved = script.get_config(DEFAULTS)
    picked = forms.select_from_list(
        [(label, key) for key, label, _ in audit.STEPS],
        title=TITLE, multiselect=True,
        prompt='Tick the steps to run. They run top to bottom.',
        checked=audit.cleanup_plan(saved['steps']))
    if picked is None:
        return None
    plan = audit.cleanup_plan(picked)
    if plan:
        # An empty checklist is not worth remembering: the next run would
        # open with nothing ticked.
        script.save_config({'steps': plan})
    return plan


def confirmed(plan):
    """Asks before anything is erased; True to go ahead."""
    erasing = [WHAT_IT_ERASES[step] for step in audit.deleting_steps(plan)]
    if not erasing:
        return True
    if len(erasing) > 1:
        what = ', '.join(erasing[:-1]) + ' and ' + erasing[-1]
    else:
        what = erasing[0]
    return forms.confirm(
        'This will %s.\n\nEntities on locked layers are left alone, and Undo '
        'takes the changes back.\n\nContinue?' % what, TITLE)


class Watch(object):
    """Progress and cancel callbacks for one step's share of the bar."""

    def __init__(self, bar, start, share):
        self.bar = bar
        self.start = start
        self.share = share
        self.stopped = False

    def progress(self, done, total):
        if not self.bar.update(self.start + self.share * float(done) / max(total, 1)):
            self.stopped = True

    def cancelled(self):
        return self.stopped


def run_step(step, watch):
    """Runs one step; returns its result for audit.format_report()."""
    if step == 'purge':
        return audit.purge(all=True)
    if step == 'audit':
        audit.audit(fix=True)
        return None                                     # queued
    if step == 'zero_length':
        handles = audit.zero_length(watch.progress, watch.cancelled)
        if watch.stopped:
            return {'cancelled': True}
        erased, skipped = audit.erase(handles)
        return {'count': erased, 'skipped': skipped}
    if step == 'duplicates':
        groups = audit.duplicates(progress=watch.progress, cancelled=watch.cancelled)
        if watch.stopped:
            return {'cancelled': True}
        erased, skipped = audit.erase(audit.duplicate_extras(groups))
        return {'count': erased, 'skipped': skipped, 'groups': len(groups)}
    if step == 'bylayer':
        changed, skipped = audit.colors_to_bylayer(watch.progress, watch.cancelled)
        if watch.stopped:
            return {'cancelled': True}
        return {'count': changed, 'skipped': skipped}
    if step == 'zoom':
        doc.send_command('_.ZOOM _E')
        return None                                     # queued
    raise ValueError('Unknown step %s' % step)


def run_steps(plan):
    """Runs the plan in order; returns [(step, result)]. A failed step is
    reported and the rest still run; Cancel stops the rest."""
    results = []
    share = 1.0 / len(plan)
    with forms.progress(TITLE, audit.step_label(plan[0])) as bar:
        for index, step in enumerate(plan):
            if not bar.update(index * share, audit.step_label(step)):
                results.extend((rest, {'not_run': True}) for rest in plan[index:])
                break
            try:
                result = run_step(step, Watch(bar, index * share, share))
            except Exception as error:
                script.get_logger().error('%s step failed: %s' % (step, error))
                result = {'error': str(error)}
            results.append((step, result))
            if isinstance(result, dict) and result.get('cancelled'):
                results.extend((rest, {'not_run': True}) for rest in plan[index + 1:])
                break
    return results


def run():
    if not has_drawing():
        toast.warning('No drawing is open', 'Open a drawing, then run Drawing Cleanup')
        return

    plan = choose_steps()
    if plan is None:
        return                                          # cancelled: say nothing
    if not plan:
        toast.info('Nothing to do', 'No steps were ticked')
        return
    if not confirmed(plan):
        return

    results = run_steps(plan)
    report = audit.format_report(results)
    forms.alert(report, TITLE, copy=report)

    line = audit.summary(results)
    if any(isinstance(result, dict) and result.get('error') for _, result in results):
        toast.warning('Drawing Cleanup hit a problem', line)
    else:
        toast.success('Drawing cleaned', line)


if '__commandpath__' in globals():
    run()
