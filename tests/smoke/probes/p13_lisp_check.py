"""Reads the files LISP wrote and checks the (pyacad ...) results."""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import smokelib as S


def body(p):
    path = os.path.join(S.run_dir(), 'lisp-result.txt')
    p.check('LISP wrote its result file', os.path.exists(path))
    if os.path.exists(path):
        text = open(path).read().strip()
        p.check('(pyacad "PYLISPRESULT" 1 "x") returned an assoc list with n = 3', '"n" 3' in text, text)
        p.check('LISP args reached __args__', '"args"' in text and '"x"' in text, text)
    nil = os.path.join(S.run_dir(), 'lisp-nil.txt')
    got = open(nil).read().strip() if os.path.exists(nil) else 'missing'
    p.check('(pyacad "PYCFGTWIN") returned nil', got == 'nil', got)

S.Probe('p13_lisp_check', globals()).run(body)
