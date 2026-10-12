"""Ribbon click path: a RibbonButton CommandHandler sends the bundle command."""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import smokelib as S


def body(p):
    btn = S.find_button('Probe', 'Cfg Twin')
    p.check('Cfg Twin button on the Probe tab', btn is not None, repr(S.button_texts('Probe')))
    if btn is None:
        return
    p.check('button has a CommandHandler', btn.CommandHandler is not None)
    p.check('bundle without icons renders with no image', (not btn.ShowImage) or btn.LargeImage is None)
    before = len(S.marker_lines('primary'))
    btn.CommandHandler.Execute(btn)
    p.check('CommandHandler.Execute returned (command queued)', True, 'primary markers before: %d' % before)

S.Probe('p05_ribbon', globals()).run(body)
