"""Ribbon survival: the pyACAD tabs are back after a workspace switch or CUILOAD. args[0] labels the run."""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import smokelib as S


def body(p):
    tabs = S.tab_titles()
    for t in ('pyACAD', 'Smoke', 'Probe'):
        p.check('tab present: ' + t, t in tabs, repr(tabs))
    p.check('pyACAD tab still has its buttons', 'Console' in S.button_texts('pyACAD'))

S.Probe('p07_tabs_' + str((list(__args__) or ['x'])[0]), globals()).run(body)
