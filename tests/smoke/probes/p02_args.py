"""PYACAD by path: __args__, __file__ and __result__ plumbing."""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import smokelib as S


def body(p):
    p.equal('__args__ from the PYACAD line', p.args, ['alpha', 'beta two'])
    p.check('__file__ points at this script', __file__.lower().endswith('p02_args.py'), __file__)
    p.check('__pyacad__ host present', __pyacad__ is not None)
    p.equal('__selfinit__ false for a plain run', globals().get('__selfinit__', False), False)

S.Probe('p02_args', globals()).run(body)
__result__ = {'seen': 2}
