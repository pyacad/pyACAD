"""Pickfirst set reaches the script (CommandFlags.UsePickSet)."""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import smokelib as S


def body(p):
    from pyacad import sel, ents
    handles = sel.current()
    p.check('pickfirst selection seen by script', len(handles) >= 3, '%d handle(s)' % len(handles))
    kinds = sorted(set(str(ents.type_of(h)).upper() for h in handles))
    p.check('selected entities are lines', kinds == ['LINE'], repr(kinds))

S.Probe('p03_selection', globals()).run(body)
