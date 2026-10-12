# -*- coding: utf-8 -*-
"""Shift+Click: forgets the steps Drawing Cleanup remembered, so its next
run opens with the default steps ticked again (everything but ByLayer
colour)."""

from pyacad import script, toast


def run():
    try:
        script.reset_config()
    except (IOError, OSError) as error:
        toast.error('Could not reset Drawing Cleanup', str(error))
        return
    toast.success('Drawing Cleanup reset', 'The next run opens with the default steps ticked')


if '__commandpath__' in globals():
    run()
