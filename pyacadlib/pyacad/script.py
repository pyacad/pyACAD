"""Information about the currently running pyACAD command.

The runtime records the command context on the PyAcadHost singleton before every
run; this module is the python-side reader. It only needs the PyAcad.Runtime
assembly (always loaded wherever pyacad scripts run), never the AutoCAD API.
"""

import clr
import re as _re

clr.AddReference('PyAcad.Runtime')

from PyAcad.Runtime.Execution import PyAcadHost


def get_host():
    """The __pyacad__ host object (works even outside a script scope)."""
    return PyAcadHost.Instance


def get_script_path():
    """Full path of the executing command's script.py."""
    return get_host().ScriptPath


def get_command_path():
    """The executing command's bundle folder (where icon.png etc. live)."""
    return get_host().CommandPath


def get_title():
    """The executing command's resolved title."""
    return get_host().CommandTitle


def get_document_path():
    """Full path of the active drawing, 'untitled' when never saved, None when
    no drawing is open."""
    return get_host().ActiveDocumentPath


def get_args():
    """The running script's __args__ (what PYACAD or (pyacad "PYNAME" args...)
    passed), read from the calling script's globals; [] when it got none."""
    import sys
    try:
        frame = sys._getframe(1)
    except (AttributeError, ValueError):
        return []
    while frame is not None:
        if '__args__' in frame.f_globals:
            return list(frame.f_globals['__args__'] or [])
        frame = frame.f_back
    return []


def get_host_version():
    """pyACAD runtime assembly version string."""
    return get_host().Version


def is_dark_theme():
    """True when the host UI renders dark - theme your dialogs with this."""
    return get_host().IsDarkTheme


def is_runner():
    """True when the script runs under ``pyacad run`` (no ribbon, no window)."""
    return bool(get_host().IsRunner)


def reload_pyacad():
    """Rescan extension folders and rebuild the pyACAD ribbon (no restart)."""
    get_host().Reload()


def get_toggle_state():
    """True when this *.toggle bundle is currently on (session-scoped)."""
    return bool(get_host().GetToggleState())


def set_toggle_state(on):
    """Sets this *.toggle bundle's state and flips its ribbon icon.

    State lives for the AutoCAD session only. There is no way to set a
    specific toggle's state from outside that toggle's own run: the state is
    keyed off the running command, which a hook or startup.py never sets. A
    toggle that must survive a restart should save the value with
    pyacad.settings and re-apply it the next time the toggle script itself
    runs.
    """
    get_host().SetToggleState(bool(on))


def get_envvar(name, default=None):
    """Session-scoped variable shared across all pyACAD scripts this session."""
    value = get_host().GetEnvVar(name)
    return default if value is None else value


def set_envvar(name, value):
    """Stores a small value for other scripts this session; None removes it."""
    get_host().SetEnvVar(name, value)


def clipboard_copy(text):
    """Puts text on the Windows clipboard."""
    clr.AddReference('PresentationCore')
    from System.Windows import Clipboard
    Clipboard.SetText(str(text))


def clipboard_text():
    """The clipboard's text, or '' when it holds none or cannot be read.

    Never raises. The clipboard is shared with every other process on the
    machine, so a read can fail simply because another application is holding
    it open, and a tool that checks it on every run must not die of that.
    """
    clr.AddReference('PresentationCore')
    from System.Windows import Clipboard
    try:
        return str(Clipboard.GetText()) if Clipboard.ContainsText() else ''
    except Exception:
        return ''


def open_url(url):
    """Opens a URL in the default browser."""
    from System.Diagnostics import Process
    Process.Start(str(url))


def show_in_explorer(path):
    """Opens Explorer at the given file (selected) or folder."""
    import os
    from System.Diagnostics import Process
    p = str(path)
    if os.path.isfile(p):
        Process.Start('explorer.exe', '/select,"%s"' % p)
    else:
        Process.Start('explorer.exe', '"%s"' % p)


def get_bundle_file(name):
    """Full path of a file inside the current command's bundle folder, or None."""
    import os
    folder = get_command_path()
    if not folder:
        return None
    path = os.path.join(folder, name)
    return path if os.path.exists(path) else None


def _config_key(bundle_key):
    """settings-store key for a bundle: cfg_ + lowercased path, runs of
    non-alphanumerics collapsed to single underscores."""
    slug = _re.sub(r'[^a-z0-9]+', '_', str(bundle_key or '').lower()).strip('_')
    return 'cfg_' + slug


def _own_key(own=None):
    """The settings/data key: a key pinned by bind(), else the ambient command's."""
    if own:
        return own
    key = get_host().CommandBundleKey
    if key:
        return _config_key(key)
    path = get_script_path()
    return _config_key(path if path else 'unknown')


def get_config(defaults, _own=None):
    """This script's saved settings merged over defaults (see pyacad.settings).

    From code that runs after script.py has returned (a modeless dialog's handlers,
    a dock panel's handlers, a timer), use bind() instead: see its docstring.
    """
    from pyacad import settings
    return settings.load(_own_key(_own), defaults)


def save_config(values, _own=None):
    """Persists this script's settings (only keys you pass survive).

    From code that runs after script.py has returned, use bind() instead.
    """
    from pyacad import settings
    settings.save(_own_key(_own), values)


def reset_config(_own=None):
    """Deletes this script's saved settings file, if any."""
    import os
    from pyacad import settings
    path = settings.path_for(_own_key(_own))
    if os.path.exists(path):
        os.remove(path)


def _data_root():
    import os
    return os.path.join(os.environ.get('APPDATA', ''), 'pyACAD', 'data')


def _data_path(per_document, suffix='json', _own=None):
    from pyacad import _datafiles
    doc_path = None
    if per_document:
        doc_path = get_host().ActiveDocumentPath
        if not doc_path:
            raise RuntimeError('Per-document data needs an open drawing.')
    return _datafiles.path_for(_data_root(), _own_key(_own), doc_path, suffix)


def get_data_file(suffix='json'):
    """Path of this tool's data file (shared across documents)."""
    return _data_path(False, suffix)


def store_data(key, value, per_document=False, _own=None):
    """Persists a JSON-serializable value under key for this tool."""
    from pyacad import _datafiles
    _datafiles.store(_data_path(per_document, _own=_own), key, value)


def load_data(key, default=None, per_document=False, _own=None):
    """Reads a value stored with store_data; default when absent."""
    from pyacad import _datafiles
    data = _datafiles.load_all(_data_path(per_document, _own=_own))
    return data.get(str(key), default)


def data_exists(key, per_document=False, _own=None):
    from pyacad import _datafiles
    return str(key) in _datafiles.load_all(_data_path(per_document, _own=_own))


class BoundScript(object):
    """This command's identity, pinned. Returned by bind().

    The module-level get_config / save_config / store_data ... ask the runtime
    "which command is running?" at the moment they are CALLED, and the runtime
    keeps answering with the most recent command long after it finished. That is
    right while script.py runs and wrong in anything that fires later: a modeless
    dialog's Click handler, a dock panel's handler, a timer. By then the user may
    have run another tool, and the call would read or overwrite THAT tool's
    settings. A BoundScript took the answer while it was still correct.
    """

    def __init__(self, own):
        self._own = own

    def get_config(self, defaults):
        return get_config(defaults, _own=self._own)

    def save_config(self, values):
        save_config(values, _own=self._own)

    def reset_config(self):
        reset_config(_own=self._own)

    def store_data(self, key, value, per_document=False):
        store_data(key, value, per_document, _own=self._own)

    def load_data(self, key, default=None, per_document=False):
        return load_data(key, default, per_document, _own=self._own)

    def data_exists(self, key, per_document=False):
        return data_exists(key, per_document, _own=self._own)


def bind():
    """Pins this command's identity for code that outlives script.py.

        me = script.bind()              # at the top of script.py
        def on_close(sender, args):     # fires whenever the user closes the dialog
            me.save_config({'width': window.Width})

    See BoundScript for why the module-level functions are wrong there.
    """
    return BoundScript(_own_key())


_loggers = {}


def get_logger():
    """A leveled logger for this script: writes to the pyACAD log; warnings and
    errors also land as colored lines in the output window when one is open."""
    from pyacad import _log
    # LogSource first: a hook or startup.py has no CommandTitle of its own, and falling
    # back to it would name whichever button ran last instead of the code that logged.
    source = (get_host().LogSource or get_title() or 'script')
    if source in _loggers:
        return _loggers[source]

    host = get_host()

    def sink(level, src, message):
        host.LogMessage(level, src, message)
        # Only echo into an output window that is ALREADY on screen. Calling
        # WriteHtml unconditionally would materialize one, so a tool that
        # prints nothing (a toast-only button, a hook) would pop an otherwise
        # empty window just because it logged a warning.
        if level in ('warning', 'error') and host.HasOpenOutput:
            css = 'pyacad-warn' if level == 'warning' else 'pyacad-error'
            from pyacad import _markdown
            host.WriteHtml('<div class="%s">%s</div>' % (css, _markdown.escape(message)))

    logger = _log.Logger(source, sink)
    _loggers[source] = logger
    return logger
