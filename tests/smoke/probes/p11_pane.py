"""Dock pane: args[0] is show | hide | expect-visible | expect-hidden."""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import smokelib as S

import clr
clr.AddReference('System.Windows.Forms')
KEY = 'Smoke.tab/Panes.panel/Pane_Demo.dockpane'


def body(p):
    from System.Windows.Forms import Application as WinForms
    action = p.args[0]
    handle = S.pane_handle(KEY)
    p.check('palette handle for ' + KEY, handle is not None)
    if handle is None:
        return
    toggle = S.find_button('Smoke', 'Pane Demo')
    p.check('ribbon toggle for the pane', toggle is not None, repr(S.button_texts('Smoke')))
    if action in ('show', 'hide'):
        handle.Visible = (action == 'show')
    WinForms.DoEvents()
    import time
    time.sleep(0.3)
    WinForms.DoEvents()
    want = action in ('show', 'expect-visible')
    p.equal('[%s] palette visible' % action, bool(handle.Visible), want)
    if toggle is not None:
        p.equal('[%s] ribbon toggle follows' % action, bool(toggle.IsChecked), want)

S.Probe('p11_pane_' + str(__args__[0]), globals()).run(body)
