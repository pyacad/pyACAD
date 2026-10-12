# -*- coding: utf-8 -*-
"""The Ask AI panel (beta): a chat that writes pyACAD tools.

Everything is in the runtime's AiChatPane. The control is built once per
AutoCAD session and handed back on every Reload, because creating a tool
triggers a Reload, and a chat that forgot itself at that exact moment would be
useless. This script only parents that control into the pane's grid.
"""

import clr

clr.AddReference('PyAcad.Runtime')
from PyAcad.Runtime.Forms import AiChatPane

pane = __pane__

host = pane.Find('Host')
host.Children.Clear()
host.Children.Add(AiChatPane.LiveContent())
AiChatPane.OnShown()
