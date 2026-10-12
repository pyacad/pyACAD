"""Create and delete a layer so the layer-changed hook fires."""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import smokelib as S


def body(p):
    from pyacad import layers
    name = 'SMOKE_LAYER'
    if layers.exists(name):
        layers.delete(name)
    layers.create(name)
    p.check('layer created', layers.exists(name))
    layers.delete(name)
    p.check('layer deleted', not layers.exists(name))

S.Probe('p09_layers', globals()).run(body)
