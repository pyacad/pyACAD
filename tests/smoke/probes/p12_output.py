"""Output window, toasts and the alert dialog placement."""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import smokelib as S


def body(p):
    from pyacad import output, toast
    from System import TimeSpan
    from System.Windows.Threading import DispatcherTimer
    from PyAcad.Runtime.Output import OutputWindow
    from PyAcad.Runtime.Forms import Toast, Dialogs
    from PyAcad.Runtime.Host import HostInfo
    from System.Windows.Interop import WindowInteropHelper

    output.print_table([['a', 1], ['b', 2]], ['name', 'n'])
    wins = S.wpf_windows()
    p.check('output window open after print_table', any(isinstance(w, OutputWindow) for w in wins),
            repr([type(w).__name__ for w in wins]))

    toast.info('smoke toast', 'detail line')
    toasts = []
    for w in S.wpf_windows():
        try:
            toasts.append((w, Toast.MessageTextOf(w)))
        except Exception:
            pass
    p.check('a toast window exists', any(t[1] == 'smoke toast' for t in toasts), repr([t[1] for t in toasts]))
    left, top, width, height = S.main_window_rect()
    for w, text in toasts:
        if text == 'smoke toast':
            right_edge, bottom_edge = w.Left + w.ActualWidth, w.Top + w.ActualHeight
            p.check('toast sits bottom-right of the AutoCAD window',
                    abs(right_edge - (left + width)) < 80 and abs(bottom_edge - (top + height)) < 120,
                    'toast r=%.0f b=%.0f host r=%.0f b=%.0f' % (right_edge, bottom_edge, left + width, top + height))

    win = Dialogs.BuildAlert('smoke alert', 'Smoke', None)
    timer = DispatcherTimer()
    timer.Interval = TimeSpan.FromMilliseconds(400)
    seen = {}

    def tick(s, e):
        timer.Stop()
        seen['owner'] = WindowInteropHelper(win).Owner
        seen['left'], seen['top'] = win.Left, win.Top
        seen['w'], seen['h'] = win.ActualWidth, win.ActualHeight
        win.Close()

    timer.Tick += tick
    timer.Start()
    win.ShowDialog()
    p.check('alert auto-closed by the probe', 'owner' in seen)
    if 'owner' in seen:
        p.check('alert owned by the AutoCAD main window', seen['owner'] == HostInfo.MainWindowHandle,
                'owner=%s host=%s' % (seen['owner'], HostInfo.MainWindowHandle))
        cx, cy = seen['left'] + seen['w'] / 2, seen['top'] + seen['h'] / 2
        p.check('alert centred on AutoCAD', abs(cx - (left + width / 2)) < 60 and abs(cy - (top + height / 2)) < 60,
                'alert centre (%.0f, %.0f) host centre (%.0f, %.0f)' % (cx, cy, left + width / 2, top + height / 2))

def cleanup():
    from PyAcad.Runtime.Output import OutputWindow
    from PyAcad.Runtime.Forms import Toast
    for w in S.wpf_windows():
        if isinstance(w, OutputWindow):
            try:
                w.Close()
            except Exception:
                pass
    try:
        Toast.CloseAll()
    except Exception:
        pass


probe = S.Probe('p12_output', globals())
probe.run(body)
cleanup()
