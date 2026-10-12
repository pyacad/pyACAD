"""Erase one entity through the API so the object-erased hook fires."""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import smokelib as S


def body(p):
    from pyacad import ents
    handles = [h for h in ents.iter() if str(ents.type_of(h)).upper() == 'LINE']
    p.check('a line to erase exists', len(handles) >= 1, '%d line(s)' % len(handles))
    if handles:
        ents.delete([handles[0]])
        p.check('erased one line', True, str(handles[0]))

S.Probe('p08_erase', globals()).run(body)
