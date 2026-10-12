"""Boot state: host year, extensions, commands, ribbon tabs, startup markers."""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import smokelib as S


def body(p):
    from pyacad import script, doc
    from PyAcad.Runtime import RuntimeHost
    p.equal('host year', int(RuntimeHost.HostYear), int(p.args[0]) if p.args else 2026)
    p.check('pyACAD version string', str(script.get_host_version()).count('.') >= 1, script.get_host_version())
    p.check('theme detected', script.is_dark_theme() in (True, False), 'dark=%s' % script.is_dark_theme())
    exts = S.extension_titles()
    for name in ('pyACAD.extension', 'Smoke.extension', 'Probe.extension'):
        p.check('extension loaded: ' + name, name in exts, repr(exts))
    cmds = S.registered_commands()
    for name in ('PYCONSOLE', 'PYRELOAD', 'PYSETTINGS', 'PYSHORTCUTS', 'PYTOGGLEDEMO',
                 'PYCFGTWIN', 'PYCFGTWINCFG', 'PYCHORDONLY', 'PYLISPRESULT', 'PYNEEDSSELECTION'):
        p.check('command registered: ' + name, name in cmds)
    tabs = S.tab_titles()
    for t in ('pyACAD', 'Smoke', 'Probe'):
        p.check('ribbon tab: ' + t, t in tabs, repr(tabs))
    smoke = S.button_texts('Smoke')
    p.check('Smoke tab shows Toggle Demo', 'Toggle Demo' in smoke, repr(smoke))
    p.check('nobutton has no ribbon presence', 'Chord Only' not in smoke)
    p.check('startup.py marker', len(S.marker_lines('startup')) >= 1)
    p.check('app-init hook marker', len(S.marker_lines('app-init')) >= 1)
    from PyAcad.Runtime.Events import AcadEvents
    p.check('object events enabled (object-erased hook present)', AcadEvents.ObjectEventsEnabled)
    p.check('sysvar read', doc.sysvar('DWGNAME') is not None, doc.sysvar('DWGNAME'))
    btn = S.find_button('pyACAD', 'Console')
    p.check('Console button has large image', btn is not None and btn.LargeImage is not None and btn.ShowImage)

S.Probe('p01_boot', globals()).run(body)
