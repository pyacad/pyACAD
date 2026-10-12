# -*- coding: utf-8 -*-
"""Plots the layouts you pick to PDF, one file per layout, in a folder you choose.

File names come from plot.batch_names, so two layouts never write the same
file. The folder, the naming and the page setup are remembered for next time.
A layout that fails to plot does not stop the rest: the failures are listed
at the end.
"""

import os

from pyacad import doc, forms, layouts, plot, script, toast

__title__ = 'Batch Plot'

_TITLE = 'Batch plot'
_PATTERNS = ['{drawing} - {layout}.pdf', '{layout}.pdf', '{drawing}_{layout}.pdf']
_OWN_SETTINGS = "Each layout's own settings"
_DEFAULTS = {'folder': '', 'pattern': _PATTERNS[0], 'page_setup': ''}


def run():
    if not _has_drawing():
        toast.warning('No drawing is open', 'Open a drawing and run Batch Plot again.')
        return
    names = layouts.names()
    if not names:
        toast.info('No layouts to plot', 'This drawing has only the model tab.')
        return
    config = script.get_config(_DEFAULTS)
    drawing = doc.path()

    picked = forms.select_from_list(names, _TITLE, multiselect=True,
                                    prompt='Layouts to plot', checked=names)
    if picked is None:
        return
    if not picked:
        toast.info('Nothing plotted', 'No layout was ticked.')
        return

    folder = forms.pick_folder('Folder for the PDFs', _start_folder(config['folder'], drawing))
    if not folder:
        return

    pattern = _ask_pattern(drawing, picked[0], config['pattern'])
    if pattern is None:
        return

    setup = _ask_page_setup(config['page_setup'])
    if setup is False:
        return

    targets = plot.batch_names(drawing, picked, folder, pattern)
    existing = [path for _, path in targets if os.path.exists(path)]
    if existing and not forms.confirm(
            '%s already in %s. Replace %s?'
            % (_count(len(existing), 'of these PDFs is', 'of these PDFs are'), folder,
               'it' if len(existing) == 1 else 'them'), _TITLE):
        return

    script.save_config({'folder': folder, 'pattern': pattern, 'page_setup': setup or ''})

    done, failures, stopped = _plot_all(targets, setup)
    _report(done, failures, stopped, len(targets), folder)


def _has_drawing():
    try:
        doc.active()
    except RuntimeError:
        return False
    return True


def _start_folder(saved, drawing):
    """The folder last plotted to, else the drawing's own folder, else none."""
    if saved and os.path.isdir(saved):
        return saved
    folder = os.path.dirname(drawing)
    return folder if folder and os.path.isdir(folder) else None


def _ask_pattern(drawing, layout, saved):
    """The naming pattern, offered as example file names for the first picked
    layout; the one used last time comes first."""
    order = sorted(_PATTERNS, key=lambda p: p != saved)
    examples = [plot.output_name(p, drawing, layout) for p in order]
    choice = forms.ask_options('Each layout plots to its own PDF. Name them like:',
                               examples, _TITLE)
    return None if choice is None else order[examples.index(choice)]


def _ask_page_setup(saved):
    """A paper space page setup name, None for each layout's own settings, or
    False on cancel. No question when the drawing has no page setups."""
    setups = layouts.page_setups(paper_only=True)
    if not setups:
        return None
    options = [_OWN_SETTINGS] + setups
    choice = forms.select_from_list(options, _TITLE, prompt='Plot with which page setup?',
                                    checked=[saved if saved in setups else _OWN_SETTINGS])
    if choice is None:
        return False
    return None if choice == _OWN_SETTINGS else choice


def _plot_all(targets, setup):
    """Plots each (layout, path); returns (plotted count, [(layout, reason)],
    True when the person cancelled part way)."""
    done = 0
    failures = []
    total = len(targets)
    try:
        with forms.progress(_TITLE, 'Starting') as bar:
            for index, (layout, path) in enumerate(targets):
                bar.check()
                bar.update(float(index) / total,
                           'Plotting %s (%d of %d)' % (layout, index + 1, total))
                try:
                    plot.to_pdf(layout, path, page_setup=setup)
                    done += 1
                except Exception as error:
                    script.get_logger().error('batch plot: %s failed: %s' % (layout, error))
                    failures.append((layout, str(error) or type(error).__name__))
            bar.update(1.0, 'Done')
    except forms.Cancelled:
        return done, failures, True
    return done, failures, False


def _report(done, failures, stopped, total, folder):
    if stopped:
        toast.info('Batch plot stopped', 'Plotted %d of %s before you cancelled'
                   % (done, _count(total, 'layout', 'layouts')))
    elif not failures:
        toast.success('Plotted %s to PDF' % _count(done, 'layout', 'layouts'), folder)
    if failures:
        if not stopped:
            toast.warning('Plotted %d of %s' % (done, _count(total, 'layout', 'layouts')),
                          '%d failed' % len(failures))
        text = '\n'.join('%s: %s' % (layout, reason) for layout, reason in failures)
        forms.alert('%s did not plot:\n\n%s'
                    % (_count(len(failures), 'layout', 'layouts'), text), _TITLE, copy=text)


def _count(n, one, many):
    return '%d %s' % (n, one if n == 1 else many)


run()
