# -*- coding: utf-8 -*-
"""Edits everything in config.json a person is meant to change, in a window.

The Shortcuts editor still owns which tool has which chord, and the pane registry
still owns which panel holds which slot, so neither is edited here. This covers
the rest: theme, the two ribbon hints, whether a bare key may be a shortcut,
where extensions load from, and the two engine paths.

Most of it shows after a Reload, which this offers. The engine paths cannot be
picked up by a Reload at all, so that case says restart instead of pretending.
"""

import clr

clr.AddReference('PyAcad.Runtime')
from PyAcad.Runtime.Forms import SettingsDialog

from pyacad import forms, script, toast


def run():
    try:
        outcome = SettingsDialog.ShowAndSave()
    except Exception as error:
        script.get_logger().error('settings not saved: %s' % error)
        toast.error('Could not save your settings', str(error))
        return

    if outcome == SettingsDialog.Outcome.Cancelled:
        return                                  # cancelled: say nothing

    if outcome == SettingsDialog.Outcome.NeedsRestart:
        toast.warning('Settings saved',
                      'The engine paths changed, so AutoCAD needs restarting')
        return

    if forms.confirm('Reload pyACAD now so the changes show?', 'Settings saved'):
        script.reload_pyacad()
        # The ribbon rebuilding behind the dialog is easy to miss, and a reload
        # that says nothing reads as a reload that did not happen.
        toast.success('Settings saved', 'pyACAD reloaded, so they show now')
    else:
        toast.success('Settings saved', 'They show after the next Reload')


if '__commandpath__' in globals():
    run()
