"""Shortcut gates as the runtime sees them right now; args[0] labels the moment."""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import smokelib as S


def body(p):
    from PyAcad.Runtime.Input import ShortcutManager
    text = ShortcutManager.Diagnose()
    p.check('[%s] keyboard hook installed' % p.args[0], ShortcutManager.HookInstalled, text)
    p.check('[%s] two chords bound' % p.args[0], 'bindings=2' in text, text)
    S.mark('shortcut-diag', p.args[0] + ' ' + text + ' seen=%d' % ShortcutManager.KeysSeen)

S.Probe('p14_shortcut_diag', globals()).run(body)
