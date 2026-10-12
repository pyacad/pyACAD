"""Command registry after Reload: args[0] is a command name, args[1] present or absent."""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import smokelib as S


def body(p):
    name, expect = p.args[0], p.args[1]
    cmds = S.registered_commands()
    p.check('%s is %s' % (name, expect), (name in cmds) == (expect == 'present'), '%d command(s)' % len(cmds))
    tabs = S.tab_titles()
    for t in ('pyACAD', 'Smoke', 'Probe'):
        p.check('ribbon tab after Reload: ' + t, t in tabs, repr(tabs))
    texts = S.button_texts('Probe')
    if expect == 'present':
        p.check('New Button on the Probe tab', 'New Button' in texts, repr(texts))
    else:
        p.check('New Button gone from the Probe tab', 'New Button' not in texts, repr(texts))

S.Probe('p06_registry_' + str(__args__[1]), globals()).run(body)
