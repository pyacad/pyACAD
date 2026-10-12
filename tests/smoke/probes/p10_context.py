"""Context gating. args[0] names the state: none | selection | paper."""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import smokelib as S


def body(p):
    state = p.args[0]
    need_sel = S.find_button('Smoke', 'Needs Selection')
    model_only = S.find_button('Probe', 'Model Only')
    paper_only = S.find_button('Probe', 'Paper Only')
    p.check('context buttons found', None not in (need_sel, model_only, paper_only),
            repr((S.button_texts('Smoke'), S.button_texts('Probe'))))
    if None in (need_sel, model_only, paper_only):
        return
    sel_on, paper = state == 'selection', state == 'paper'
    p.equal('[%s] Needs Selection enabled' % state, bool(need_sel.IsEnabled), sel_on)
    p.equal('[%s] Model Only enabled' % state, bool(model_only.IsEnabled), not paper)
    p.equal('[%s] Paper Only enabled' % state, bool(paper_only.IsEnabled), paper)

S.Probe('p10_context_' + str(__args__[0]), globals()).run(body)
