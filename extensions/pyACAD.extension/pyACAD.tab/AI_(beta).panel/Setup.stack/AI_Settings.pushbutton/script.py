# -*- coding: utf-8 -*-
"""Opens the Settings window scrolled to the AI assistant section.

The section itself is part of Settings, so there is one place where every
pyACAD setting lives. This button is the short way there from the AI panel.
"""

import clr

clr.AddReference('PyAcad.Runtime')
from PyAcad.Runtime.Forms import SettingsDialog

from pyacad import forms, script, toast


def run():
    try:
        outcome = SettingsDialog.ShowAndSave('ai')
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

    # The AI keys take effect at the next message with no Reload, but the same
    # window edits everything else too, so offer the Reload the Settings button does.
    if forms.confirm('Reload pyACAD now so the changes show?', 'Settings saved'):
        script.reload_pyacad()
        toast.success('Settings saved', 'pyACAD reloaded, so they show now')
    else:
        toast.success('Settings saved', 'Ask AI uses them from your next message')


if '__commandpath__' in globals():
    run()
