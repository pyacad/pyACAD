# Writing pyACAD tools

pyACAD turns folders on disk into ribbon buttons inside Autodesk AutoCAD. A folder
named `Thing.pushbutton` containing `script.py` is a working command: no manifest, no
registration, no build step. Scripts run on IronPython 3.4 (Python 3 syntax, .NET
interop through `clr`) on the AutoCAD UI thread, with the `pyacad` library on the
path and the AutoCAD .NET API (acdbmgd, acmgd, accoremgd) loadable through `clr`.

## A pushbutton bundle

```
<Name>.pushbutton/
    bundle.yaml     title, tooltip, optional shortcut, keytip, engine, context
    script.py       runs on click; top-level code, no __main__ guard
    config.py       optional; Shift+Click runs it (settings for the tool)
    icon.png ...    pyACAD generates these for AI-made tools
```

`bundle.yaml` is not real YAML: one `key: value` per line, the first colon splits the
line, everything after it is the value, indentation is meaningless.

```yaml
title: Isolate Level
tooltip: Hides everything that is not on the selected items' level.
shortcut: Ctrl+Shift+L        # optional; needs Ctrl or Alt
context: selection            # optional; greys the button when false
```

`context:` is a boolean expression over `doc`, `selection`, `modelspace`,
`paperspace`, `xrefs`, `blocks`, `layouts` and `multi-doc`, combined with `&`,
`|` and `!` (`&` binds tighter, no parentheses).

## The script contract

- `script.py` runs top to bottom on every click. Put the work at top level or in a
  function you call at the end. `if __name__ == '__main__':` never runs in pyACAD.
- Globals available: `__file__`, `__commandpath__` (the bundle folder), `__title__`.
- Everything the person sees goes through `pyacad.toast` (one line), `pyacad.forms`
  (a question), `pyacad.banner` (one result on a bar) or `pyacad.output` (a table or
  report). `print()` opens an output window: never use it for status.
- The AutoCAD API is not thread-safe. No threads, no async, no timers touching the
  model.
- Wrap bulk edits in one undo step so the person gets one Ctrl+Z, not thousands.
- Every outcome reports: success, failure and "nothing to do" all toast. Cancelling a
  dialog is the one silent exit.
- No em dashes in any user-facing text. Sentence case, capitalised first letter.

## Rules and traps

### Hard rules

These are project rules, not preferences. Violating them produces tools that look broken.

- **Never `print()` for status.** The output window is created lazily on first write, so
  a bare `print()` opens a window containing one line. Use `pyacad.toast` for one-line
  results. Use `pyacad.output` only when there is genuinely a table or a report to show.
- **Never put work inside `if __name__ == '__main__':`.** It never runs. Measured:
  `__name__` is `'builtins'` on IronPython and `''` on CPython, never `'__main__'`. The
  script silently does nothing and reports no error.
- **Cancelling is silent.** `forms.ask_string`, `save_file` and `open_file` return `None`
  on cancel; `forms.confirm` returns `False`. Exit without a toast. Never say "cancelled".
- **Report every outcome.** A tool that finishes with no visible result reads as broken.
  Success, failure, and "nothing to do" all get a toast.
- **One undo step per user action.** Wrap bulk edits in one transaction. Thousands of
  undo entries is a bug.
- **No threads touching the model.** The AutoCAD API is not thread-safe and your script
  runs on the UI thread.
- **Never launch AutoCAD yourself, and that includes `pyacad run`.** Ask the user to
  click. Verify from the log instead.
- **A script that may run under `pyacad run` checks `__runner__` before any prompt or
  dialog;** under the runner dialogs raise and toasts go to the transcript.
- **No em dashes** in any user-facing string, and none in these docs either.

### Splitting pure logic from API calls

The library's own modules do this and so should any tool of real size: a pure half that
takes plain dicts and lists and is unit-testable outside AutoCAD, plus a thin API half
that reads the document and calls it.

Put the pure half in the bundle folder or in `<extension>/lib/` and import it. Both are on
the module search path and both are re-read on every run.

### What needs what to take effect

| Changed | Needed |
|---|---|
| `script.py`, `config.py`, bundle-local modules, `<extension>/lib/` modules, files already inside an existing `*.lib` folder | Nothing, just run it again |
| `bundle.yaml`, icons, folder names, shortcuts, new bundles, `hooks/*.py`, `startup.py` | Click Reload |
| `pyacadlib/` | Click Reload |
| A `*.lib` folder added or removed | Restart AutoCAD |
| C# under `src/` | Rebuild and restart AutoCAD |

### Failure modes that are silent

Check these first when a bundle does not appear or does nothing:

- A `.pushbutton` with no `script.py` is dropped with no message.
- A `.stack` needs exactly 2 or 3 pushbuttons; otherwise it is skipped.
- A `.pulldown` with no usable pushbuttons is skipped.
- `.extension` folders are found only directly under a root, never nested deeper.
- A tool in the wrong place is missing from its container's `layout:` list, or the entry
  does not match the folder name. Unlisted folders go last, in name order. A misspelt
  entry is reported in the log and the problem list.
- A single-digit prefix is not stripped from the title: `1_Keep` displays as "1 Keep".
- A bad `engine:` value fails at click time, not load time.
- Icon variants fall back to `icon.png` only. Shipping only `icon.dark.png` gives you
  no icon at all.
- A hook that fails 3 times in a row is silently disabled until Reload; the log has every
  traceback, the toast only fires once, on the 3rd failure.
- A `context:` button that never enables usually has a misspelled condition name (logged) or
  a rule that fails to parse (also logged, and the button is left always-enabled, not
  always-disabled).
- `pyacad.script.get_toggle_state`/`set_toggle_state` only work called from that toggle's own
  `script.py`/`config.py`; a hook, `startup.py`, or a smartbutton's `__selfinit__` run cannot
  target a specific toggle.
- A `.dockpane` with no `pane.xaml` is dropped (logged); its `script.py` is optional, the
  reverse of a pushbutton.
- A `*.dockpane` is parsed but not shown on the ribbon in this release.
- A new `*.lib` folder needs an AutoCAD restart, not a Reload, before anything can import
  from it.

the sections below has the full list with the log line for each.

## Templates
Copy-paste starting points. Replace the parts in angle brackets and delete what you do
not need. `<the objects to work on>` stands where a tool reads the drawing: pyacad has no
AutoCAD selection or document module yet, so read them through the AutoCAD .NET API.

### Minimal script.py

The smallest useful tool: act on the selection, report the outcome, handle the empty case.

```python
"""<One sentence. This becomes the tooltip when there is no bundle.yaml.>"""
from pyacad import toast

items = <the objects to work on>

if not items:
    toast.info('Nothing selected', 'Select something in the drawing and run this again.')
else:
    # <do the work>
    toast.success('<Verb>ed %d item(s)' % len(items))
```

Top-level code only. Do not wrap this in `if __name__ == '__main__':`, which never runs.

### script.py with a file export

Note the silent exit on cancel: no toast, because cancelling is not an error.

```python
"""Exports the current selection to CSV."""
import csv

from pyacad import forms, toast

items = <the objects to work on>

if not items:
    toast.info('Nothing selected', 'Select something first.')
else:
    path = forms.save_file(default_name='selection.csv')
    if path is not None:                       # None means cancelled: say nothing
        try:
            with open(path, 'wb') as handle:
                writer = csv.writer(handle)
                writer.writerow(['Name', 'Type'])
                for item in items:
                    writer.writerow([<name of item>, <type of item>])
        except (IOError, OSError) as ex:
            toast.error('Could not write the file', str(ex))
        else:
            toast.success('Exported %d row(s)' % len(items), path)
```

### script.py with settings

Pairs with the `config.py` below. `TOOL` and `DEFAULTS` are module constants so both
files agree on them; for a bigger tool put them in a shared module in the bundle folder.

```python
"""Reports the selection size and warns when it is unusually large."""
from pyacad import settings, toast

TOOL = '<tool_key>'
DEFAULTS = {'warn_above': 1000}

values = settings.load(TOOL, DEFAULTS)         # never raises; bad file yields defaults
items = <the objects to work on>

if not items:
    toast.info('Nothing selected', 'Select something first.')
elif len(items) > values['warn_above']:
    toast.warning('%d item(s) selected' % len(items), 'That is a large selection.')
else:
    toast.success('%d item(s) selected' % len(items))
```

### config.py (the Shift+Click secondary action)

```python
"""Options for <Tool name>."""
from pyacad import forms, settings, toast

TOOL = '<tool_key>'
DEFAULTS = {'warn_above': 1000}

values = settings.load(TOOL, DEFAULTS)
answer = forms.ask_string(
    'Warn when the selection is larger than:',
    default=str(values['warn_above']),
    title='<Tool name>',
)

if answer is not None:                         # None means cancelled: change nothing
    try:
        values['warn_above'] = int(answer)
    except ValueError:
        toast.error('That is not a whole number')
    else:
        settings.save(TOOL, values)
        toast.success('Saved', 'Warning above %d items.' % values['warn_above'])
```

### script.py with progress over a long loop

Counted progress, not indeterminate, because the total is known.

```python
"""<What it does.>"""
from pyacad import output, toast

items = <the objects to work on>

if not items:
    toast.info('Nothing selected', 'Select something first.')
else:
    total = len(items)
    rows = []
    for index, item in enumerate(items):
        rows.append([<name of item>, <type of item>])
        if index % 100 == 0:
            output.progress(float(index) / total, 'Reading %d of %d' % (index, total))
    output.progress(1.0)

    output.print_table(rows, ['Name', 'Type'])
    toast.success('Listed %d item(s)' % total)
```

`output.progress` and `output.print_table` both write to the output window, so the window
opens. That is correct here, because there is a table to show. If the tool only produced a
count, it should toast and open nothing.

### Splitting pure logic from the API

Put the pure half in the bundle folder or `<extension>/lib/`, import it from `script.py`.
Both locations are on the module search path and both are re-read on every run.

`rules.py`, testable with plain CPython outside AutoCAD:

```python
"""Pure grouping rules. No AutoCAD import, so this is unit-testable anywhere."""

def group_by_prefix(names, separator='-'):
    """Groups names by their leading segment. Returns {prefix: [name, ...]}."""
    groups = {}
    for name in names:
        prefix = name.split(separator)[0].strip() or '(none)'
        groups.setdefault(prefix, []).append(name)
    return groups
```

`script.py`, the thin API half:

```python
"""Groups the selection by name prefix and reports the counts."""
from pyacad import output, toast

import rules                                   # bundle-local, re-read every run

items = <the objects to work on>

if not items:
    toast.info('Nothing selected', 'Select something first.')
else:
    groups = rules.group_by_prefix([<name of i> for i in items])
    output.print_table(
        sorted([[prefix, len(names)] for prefix, names in groups.items()]),
        ['Prefix', 'Count'],
    )
    toast.success('%d group(s)' % len(groups))
```

### bundle.yaml

Eight keys are read on a pushbutton-shaped bundle. Anything else is parsed and ignored.

```yaml
title: <Button caption>          # a literal \n breaks a long caption onto two lines
tooltip: <One or two sentences. The shortcut is appended automatically.>
shortcut: Ctrl+Shift+<K>
keytip: <XY>
engine: ironpython
context: selection & modelspace    # optional: & | !, & binds tighter, no parens
min_host_version: 2024              # optional: bare year, either or both
max_host_version: 2026
```

Remember it is not real YAML: a trailing `# comment` is kept as part of the value,
indentation is meaningless, the first colon splits the line, and duplicate keys mean the
last one wins.

A pulldown, splitbutton or splitpushbutton reads only `title`, `tooltip` and `keytip`. A
`.stack` folder reads no yaml at all. A urlbutton reads `title`, `tooltip`, `keytip` and
requires `url`; a linkbutton reads the same three and requires `plugin` (running it is
not supported yet). A dockpane reads `title`, `tooltip`, `engine` and `keytip`, and requires `pane.xaml`
in the folder rather than any yaml key; `shortcut:` on a dockpane is parsed but bound to
nothing in this release.

### extension.yaml

```yaml
name: <Extension name>
engine: ironpython
```

Two keys, no more. `engine` here becomes the default for every pushbutton in the
extension, and each bundle can still override it.

### Guarded import

Use this when a script must still do something useful outside a live AutoCAD session,
for example so a dialog can open with no drawing. Loading the AutoCAD API raises
outside AutoCAD.

```python
import clr
try:
    clr.AddReference('acmgd')
    import Autodesk.AutoCAD.ApplicationServices as acad
except Exception:
    acad = None
```

### hooks/\<event\>.py

Filename minus `.py` must be one of the twenty exact event names: `doc-created`,
`doc-opened`, `doc-activated`, `doc-closing`, `doc-saved`, `command-will-start`,
`command-ended`, `command-cancelled`, `command-failed`, `before-command`, `after-command`,
`selection-changed`, `object-appended`, `object-modified`, `object-erased`, `layer-changed`,
`layout-switched`, `app-init`, `app-quit`, `system-variable-changed`. Note that
`before-command`/`after-command` wrap a pyACAD button run, while `command-*` are AutoCAD's
own commands. Four globals: `__file__`, `__runner__`, `__pyacad__`
and `__event__`; no `__commandpath__`, no `__title__`, no `__selfinit__`. `__runner__` is the
live runner flag, in practice False (a `pyacad run` AutoCAD wires no hooks). Output goes
to the log, never a window, so use `toast` for anything the user should see.

```python
from pyacad import toast

toast.info('Event: ' + __event__['name'])
```

Keep it fast: hooks run synchronously on the UI thread with a 500ms soft budget (over-budget
runs are logged, not killed), and 3 consecutive failures disable the hook until Reload.

### startup.py

One per extension, at the extension root, beside `extension.yaml`. Runs at boot and every
Reload, before that extension's ribbon builds. Same globals as a hook minus `__event__`:
`__file__`, `__runner__` (True only when `pyacad run` starts it) and `__pyacad__`.
A raised exception is logged and toasted, but the extension's buttons still load.

```python
"""Confirms a shared helper resolved before anyone clicks a button."""
from pyacad import toast

try:
    import acme_common
except ImportError:
    toast.warning('acme_common not found', 'Check the Shared.lib folder is installed.')
```

### \*.smartbutton script.py

Runs twice: once at ribbon build with `__selfinit__` True and a `__button__` proxy, once per
click with `__selfinit__` False and no `__button__` at all. Always guard on `__selfinit__`.

```python
if __selfinit__:
    __button__.Title = 'Count (0)'
    __button__.Enabled = True
else:
    from pyacad import toast
    toast.info('Clicked!')
```

### \*.toggle script.py

Ships `icon.on(.dark).png` / `icon.off(.dark).png` beside the usual pair. `icon.off.png` falls
back to `icon.png`, and `icon.off.dark.png` falls back to `icon.off.png` first, then `icon.png`
(the `.on` pair follows the same chain, not an independent fallback to `icon.png`). State is
session-scoped and keyed to this bundle only -
`get_toggle_state`/`set_toggle_state` do not work called from anywhere else.

```python
"""Turns grid snapping on and off."""
from pyacad import script, toast

on = not script.get_toggle_state()
script.set_toggle_state(on)
toast.info('Snap ' + ('on' if on else 'off'))
```

A toggle whose caption names an action ("Hide Tabs") should name the other action while on:
`title_on:` and `tooltip_on:` in its bundle.yaml swap with the icon and survive Reload. Only
`*.toggle` reads them.

```yaml
title: Hide\nTabs
title_on: Show\nTabs
tooltip: Takes the tabs you choose off the ribbon.
tooltip_on: Puts the tabs you hid back on the ribbon.
```

### \*.dockpane bundle

A dock panel, not a script that runs and returns. `pane.xaml` is required and its root is a
plain element (a `Grid` or `StackPanel`, never a `Window`); `script.py` is optional and runs
once, when the panel's content is built, with `__pane__` on top of the usual globals. The
ribbon button is a toggle that follows the panel's visibility; `icon.on(.dark).png` beside
the standard four is the pressed art while the panel is open.

```
<NN>_<Name>.dockpane/
    pane.xaml             # required; plain element root
    script.py             # optional; runs once at content build, gets __pane__
    bundle.yaml           # optional; title, tooltip, engine, keytip
    icon.png              # 96x96, plus the dark and small variants as usual
    icon.on.png           # pressed state while the panel is open
    icon.on.dark.png
```

`pane.xaml`:

```xml
<StackPanel xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
            Margin="16">
  <TextBlock x:Name="Headline" FontSize="16" FontWeight="SemiBold" Text="Panel title" />
  <TextBlock x:Name="Status" FontSize="11.5" Margin="0,4,0,0" Text="Ready" />
  <Button    x:Name="Close" Margin="0,12,0,0" Padding="8,4,8,4" Content="Hide this panel" />
</StackPanel>
```

`script.py`:

```python
"""<What the panel shows.>"""
import datetime

pane = __pane__

pane.Find('Status').Text = 'Built at ' + datetime.datetime.now().strftime('%H:%M:%S')

def on_close(sender, args):
    pane.Visible = False

pane.Find('Close').Click += on_close
```

Handlers wired here fire long after this run finished, so inside them act through `__pane__`
(held in the `pane` variable), never through whichever command ran last.

### Folder layout for a finished bundle

```
<NN>_<Name>.pushbutton/
    script.py             # required; primary action
    config.py             # optional; Shift+Click
    bundle.yaml           # optional; title, tooltip, shortcut, keytip, engine
    rules.py              # optional; bundle-local pure module
    icon.png              # 96x96
    icon.dark.png         # 96x96
    icon.small.png        # 32x32
    icon.small.dark.png   # 32x32
```

## Design rules
The full design system is `docs/design-system/` (v2.2). This is the subset that applies
when you are writing a bundle script, plus the behavioural rules that decide whether a
tool feels finished.

### Choosing a surface

Pick the lightest surface that carries the information. Most tools need only the first.

| Surface | Use it for | Blocks? |
|---|---|---|
| `toast` | The result of a finished action, one line | No |
| `forms` | A question you must have answered to continue | Yes |
| `output` | Results too big for a line: tables, lists, links, progress | No |
| A `*.dockpane` panel | Live state the user keeps docked and glances at while working | No |
| Custom WPF dialog | A real tool with many settings; built on `src/PyAcad.Runtime/Forms/DesignSystem.cs` | Yes |

Rules that follow from this:

- **Never open a window to say one line.** `print()` opens the output window; a bare
  `print('Done')` gives the user a window containing "Done". Toast it instead.
- **Never toast a table.** A toast is one line plus an optional muted detail line.
- **Never ask a question in a toast.** Toasts carry no buttons and dismiss themselves.

### Toast levels

```python
toast.success(message, detail=None)   # green   the thing worked
toast.error(message, detail=None)     # red     the thing failed
toast.info(message, detail=None)      # blue    nothing to do, or neutral fact
toast.warning(message, detail=None)   # amber   worked, but with a caveat
toast.show(level, message, detail=None)
```

### Behavioural rules

**Report every outcome.** Success, failure, and "nothing to do" all produce a toast. A
tool that finishes silently reads as broken, and the user will click it again.

**Cancelling is silent.** `ask_string`, `save_file` and `open_file` return `None` on
cancel; `confirm` returns `False`. Exit with no message. Never toast "cancelled".

Test for `None` explicitly, because an empty string is a different answer:

```python
## right
if path is not None:
    ...

## wrong: an empty string from ask_string is a real answer, not a cancel
if path:
    ...
```

**Empty states are real states.** "Nothing selected" gets a toast that says what to do
about it, not a silent success.

**Handle predictable failures, let genuine bugs raise.** A missing file or a bad number is
a toast with a plain sentence. A programming error should raise, so the traceback lands in
the output window where it can be read.

**One undo step per user action.** Wrap bulk edits in one transaction.

**Counted progress, not indeterminate,** whenever the total is known. Offer cancellation
for anything that can run more than a few seconds.

**Speak the user's language.** Say what they see in AutoCAD, not the API type name.
"3 layers" not "3 LayerTableRecords".

### Copy rules

- Sentence case everywhere. Not Title Case, not ALL CAPS.
- **No em dashes or en dashes.** Use a comma, a colon, or two sentences.
- No emoji in any UI string.
- Plain verbs: "Export", "Group", "Rename". Not "Let's export!".
- Numbers get thousands separators when they can get large.
- Name the thing that failed and what to do next, in one sentence.

### Visual rules, if you write your own dialog

Read `docs/design-system/` properly before building a dialog. The short version:

- The Windows accent colour appears in exactly three places: the primary action, the
  selection rail, and the focus ring. Nowhere else.
- Selection background is a neutral wash, never an accent tint.
- Solid surfaces only. No gradients, no glass, no translucency, no alpha-tinted fills.
- Hairlines and whitespace carry structure, not shadows. The single exception is a popup
  floating over its own window.
- Native affordances: real checkboxes, real toggles, a segmented control for 2 to 4
  exclusive modes.
- 32px control height, 32px list row height, 4px corner radius, 8px control gap, 24px
  section gap. 24px page padding for prose and settings windows, 16px for data-dense ones.
- Segoe UI Variable; 13px body, 11.5px floor. Consolas for script output.
- Numbers are quiet tabular text, not coloured chips. The only chip in the system is a
  status tag, because status is categorical.
- Read `pyacad.script.is_dark_theme()` and theme accordingly.

Banned outright, from the design system's "slop test": gradients, glassmorphism,
accent-alpha washes, pill soup, badge marketing, emoji, hero cards for a two-way choice,
centered content in rows and forms, hover-only affordances, decorative motion,
opacity-as-disabled, mouse-only controls, a control per row instead of virtualising,
blank waiting states, and hand-drawn OS chrome.

### The HTML output window

The one CSS surface in the project. Its custom properties are emitted server-side per
theme, so use the real emitted names in custom markup:

```
--bg  --fg  --err  --surface  --line  --frame  --muted  --row-hover  --accent
--err-wash  --err-line  --chart-1 .. --chart-6
```

Classes you can target: `.pyacad-card`, `.pyacad-card.err`, `pre.pyacad-text`,
`pre.codeblock`, `table.pyacad` (with `td.num` / `th.num`), `a.pyacad-el`,
`#pyacad-progress`.

Notes:

- `print_md` HTML-escapes its input before converting, so raw markup in your data can
  never become live HTML.
- `table_html` decides numeric alignment from the data, not the column position: a column
  is numeric when every non-empty cell parses as a float after stripping commas.
- `element_link(item, label)` returns a string; embed it yourself with `print_html`.
- Charts go through `output.chart_bar`, `chart_line`, `chart_pie` and `chart_doughnut`:
  theme-aware SVG cards whose series colours are `var(--chart-N)`. Hand-write markup into
  `print_html` only for a shape those four do not cover.
- The window degrades to plain text without the WebView2 runtime. Never branch on this.

### Icons

Four PNGs per bundle: `icon.png` and `icon.dark.png` at 96x96, `icon.small.png` and
`icon.small.dark.png` at 32x32. Every variant falls back to `icon.png` only, so shipping
just a dark variant gives you nothing. A `*.toggle` adds `icon.on(.dark).png` and
`icon.off(.dark).png` for its two states; a `*.dockpane` adds `icon.on(.dark).png` as the
pressed art while its panel is open. All of it comes from the same generator.

Generated by `tools/make_icons.py`, never hand-edited. Register the bundle path in its
`ICONS` dict with a draw function and re-run it. The drawing language: neutral ink draws
the thing, a solid accent mark draws the action, so colour always means "what this tool
does". Small glyphs are separate simplified drawings with a heavier relative stroke, not
resized copies. Alert red appears exactly once in the whole shipped set, on Purge, the only
destructive tool. Do not add a second.

## The pyacad library
Every signature here was read from `pyacadlib/pyacad/*.py`. Defaults are transcribed
literally.

### Module index

| Module | Purpose | Import-time requirement |
|---|---|---|
| `output` | Output-window HTML, tables, charts, images, progress | None (stdlib only); calls need the runtime |
| `settings` | Per-tool JSON settings under `%APPDATA%` | None (stdlib only) |
| `xl` | Read/write `.xlsx` workbooks, no Excel required | None (stdlib only) |
| `forms` | Modal dialogs, pickers, cancellable progress, XAML windows | `PyAcad.Runtime` only |
| `script` | Running-command context, session vars, per-command settings/data, logger, theme, reload | `PyAcad.Runtime` only |
| `toast` | Corner status toasts | `PyAcad.Runtime` only |
| `banner` | One result on a full-width bar along the bottom of the window (same calls as toast, plus `prompt()` that stays and `clear()`); for the one value a tool exists to produce or the instruction a pick is waiting on, never routine status | `PyAcad.Runtime` only |

`PyAcad.Runtime` is always loaded wherever a pyACAD script runs, so that tier never fails
in a bundle. The **live AutoCAD** tier imports `pyacad._api` at module load; outside a
session that raises:

```
ImportError: pyacad: could not load the AutoCAD .NET API (acdbmgd, acmgd, accoremgd).
This module only works inside an AutoCAD session running pyACAD.
```

Use a guarded import when a tool must still open a dialog without a model:

```python
from pyacad import forms, settings, toast
try:
    from pyacad import _api
except ImportError:
    _api = None
```

`import pyacad` exposes exactly one public name, `pyacad.__version__`, the release number as a string (the same number Settings and the installer show).
There are no re-exports, no `__all__`, no automatic submodule import. `pyacad.forms` after a
bare `import pyacad` is an `AttributeError`. House style, used by every shipped bundle, is
`from pyacad import ...` with modules in alphabetical order, at module level, immediately
after the docstring:

```python
from pyacad import forms, output, toast
```

**Off limits:** `_api` (except for the guarded import above), `_atomic`, `_charts`,
`_datafiles`, `_history`, `_log`, `_markdown`, `_repl`, `_util`, `_xlsx`. They are
implementation and change without notice.

### Cross-cutting conventions

**Error reporting styles.** `settings.load` never raises and never reports; the
`output` and `forms` calls raise on genuine bugs; importing `_api` raises `ImportError`
outside AutoCAD.

`ProgressScope` is **not** a `pyacad` module. It is the C# type
`PyAcad.Runtime.Forms.ProgressScope`, imported directly:

```python
from PyAcad.Runtime.Forms import ProgressScope
scope = ProgressScope.Begin('Working', 'Starting')   # modal-ish: disables the host window
try:
    scope.Report(0.5, '5,000 of 10,000')                      # pumps the UI at most every UiPump.IntervalMs
finally:
    scope.Dispose()                                           # re-enables the host window
```

Always dispose in a `finally`. `ScriptExecutor` calls `ProgressScope.CloseAll()` in its own
`finally` as a backstop, but a live scope means an unclickable AutoCAD until then.

### forms

Import needs `PyAcad.Runtime` only, never the AutoCAD API, so dialogs open while a model
is still loading. **Every one of these blocks** on a modal WPF dialog.

```python
forms.alert(message, title='pyACAD', copy=None)          # -> None. copy=str adds a Copy
                                                          #    button (clipboard, dialog stays)
forms.confirm(message, title='pyACAD')                   # -> bool. True on Yes.
                                                          #    FALSE ON CANCEL/No.
forms.ask_string(prompt, default='', title='pyACAD')     # -> str, or NONE on cancel
forms.save_file(filter='CSV files (*.csv)|*.csv|All files (*.*)|*.*',
                default_name='', title='Save file')       # -> str path, or NONE on cancel
forms.open_file(filter='All files (*.*)|*.*',
                title='Open file')                        # -> str path, or NONE on cancel

forms.select_from_list(items, title='Select', multiselect=False, prompt=None, checked=None)
    # items: strings, or (label, value) pairs. Searchable list.
    # checked: values to open with ticked/picked (a saved choice); unknown values ignored.
    # -> picked value, or [values] with multiselect=True, or NONE on cancel
    #    (a cancelled multiselect is None, never an empty list).

forms.ask_options(prompt, options, title='pyACAD')
    # -> the chosen entry from options itself (not its index), or NONE on cancel.
    #    prompt=None -> the chromeless quick switch (no titlebar, no question;
    #    Esc or clicking outside cancels). Only for self-explanatory labels.

forms.ask_number(prompt, default=None, min_value=None, max_value=None, title='pyACAD')
    # -> float, or NONE on cancel. The dialog itself refuses OK outside
    #    [min_value, max_value], so the script never sees an out-of-range answer.

forms.pick_folder(title='Select folder', initial=None)
    # -> str path, or NONE on cancel. Standard Windows folder browser.
```

`message` and `prompt` are passed through `str()`, so non-string values are safe.

**TRAP: test for `None`, not falsiness.** An empty string from `ask_string` is a real answer,
distinct from a cancel:

```python
answer = forms.ask_string('New name:')
if answer is not None:          # right
    ...
if answer:                      # wrong: swallows a deliberate empty answer
    ...
```

`confirm` cannot distinguish "No" from "cancelled". Both are `False`.

Cancelling is silent by house rule: exit without a toast, never say "cancelled".

The keyword is literally `filter`, which shadows the builtin inside the call only.

**`forms.progress` is a context manager, distinct from `output.progress`.** It shows a modal,
cancellable progress window that blocks the rest of AutoCAD, which is what a batch the user
started on purpose (and may want to stop) calls for; `output.progress` never blocks and has no
Cancel button.

```python
forms.Cancelled                    # Exception subclass, raised by p.check()

with forms.progress('Exporting', 'Starting') as p:
    for index, item in enumerate(items):
        p.check()                          # raises forms.Cancelled once Cancel is clicked
        export_one(item)
        p.update(float(index) / len(items), '%d of %d' % (index, len(items)))
        # p.update(...) ALSO returns False once cancelled, for callers that would
        # rather test a return value than catch an exception.
```

The window closes on every path out of the `with` block: normal completion, a propagating
`Cancelled`, or any other exception. Catch `Cancelled` **outside** the `with`, not inside it,
or the window stays open for the rest of the script.

**`forms.WPFWindow(xaml_file)`** loads a `.xaml` file shipped in the current command's bundle
folder and wraps it as a live WPF window, for a settings form with more fields than a couple of
chained prompts can carry cleanly.

```python
win = forms.WPFWindow('layout.xaml')     # resolves against script.get_command_path();
                                         # IOError immediately if the file is missing
win.find(name)                          # -> the named XAML element, or None
win[name]                               # same lookup; RAISES KeyError instead of None
win.show_dialog()                       # modal, owned by AutoCAD, runtime chrome applied
                                         # -> DialogResult
win.close(result=None)                  # True/False sets DialogResult first, then closes
```

Once loaded, every element is a live .NET object: set `.Text`, wire `.Click +=`, read
`.IsChecked`, exactly as in WPF code-behind. `WPFWindow` only solves loading the file and
applying the runtime's dialog chrome; it is not a data-binding layer. Wire every event handler
and set every initial value **before** `show_dialog()`: nothing done to the window after that
call returns takes effect until the next run.

### output

Import needs nothing beyond the stdlib. `table_html` and `format_table` are pure and
unit-testable. The rest go through `script.get_host()`.

```python
output.print_html(html)                 # -> None. Appends an HTML fragment.
output.print_md(markdown)               # -> None. Converts then appends.
output.print_table(rows, headers)       # -> None. print_html(table_html(rows, headers)).
output.progress(fraction, label='')     # -> None. fraction 0..1; 1.0 completes the bar.
output.element_link(item, label)        # -> str of HTML. A link that selects the object.
output.table_html(rows, headers)        # -> str. PURE.
output.format_table(rows, headers)      # -> str. PURE. Aligned plain text with a dashed rule.

output.chart_bar(labels, values, title=None)        # -> None. Theme-aware SVG card.
output.chart_pie(labels, values, title=None)        # -> None.
output.chart_doughnut(labels, values, title=None)   # -> None. chart_pie with a hole.
output.chart_line(labels, series, title=None)       # -> None. series: {name: [values]}
                                                    #    sharing one labels axis.
output.print_image(path, caption=None)   # -> None. png/jpg/jpeg/gif/svg as a data URI.
                                         #    RAISES ValueError for any other extension.
output.print_code(text)                 # -> None. Escaped monospace block, horizontal scroll.
output.save(path)                       # -> None. Everything printed so far, as standalone
                                         #    HTML that opens in a browser with nothing running.
output.set_title(text)                  # -> None. Retitles the window (default is
                                         #    'pyACAD - <button title>').
```

All four chart functions print through `print_html` under the hood, so calling one opens or
appends to the output window exactly like any other `print_*` call. Every series colour is
`var(--chart-N)` (N wraps 1..6), pulled from the same CSS custom properties the rest of the
window themes with, so the identical markup renders correctly light or dark.

**TRAP: `chart_line` truncates a longer series to the label count, and scales from that
truncated set.** A series with more values than `labels` only has its leading values drawn; the
vertical peak is computed from those same drawn values, never from the untruncated series, so a
long tail nobody sees cannot compress the visible lines by inflating the scale.

**Any of the first five opens the output window**, lazily, on the first write. The window is
titled `pyACAD - <button title>`. A script that writes nothing opens no window, which is why
one-line statuses belong in `toast` and never in `print()`.

**TRAP: outside a run these silently do nothing.** `PyAcadHost.WriteHtml` and `.Progress` are
`_currentOutput?.WriteHtml(...)`, a null-conditional call. `element_link` returns the plain
`label` when there is no output window. No exception, no output.

`table_html` **decides numeric alignment from the data, not the column position.** A column is
tagged `class="num"` when every non-empty cell parses as a float after `.replace(',', '')` and
at least one cell is non-empty. Mixed columns are left-aligned. Column count follows `headers`;
short rows are padded with `''`; extra cells in a row are ignored. Every header and cell is
HTML-escaped.

`print_md` HTML-escapes its input **before** converting, so raw markup in your data can never
become live HTML. It covers `#`/`##`/`###` headers, `**bold**`, `*italic*`, `` `code` ``,
```` ``` ```` blocks, `- ` lists, `[text](url)` links and paragraphs, and nothing else.

`element_link` returns a string; embed it yourself:

```python
output.print_html('Largest: ' + output.element_link(item, label))
```

TRAP: passing link HTML through `print_table` escapes it into visible markup. Build the table
by hand with `print_html` when a cell needs to be a link, the way `memory.contents_html` does.

### script

Import needs `PyAcad.Runtime` only. Never touches the AutoCAD API.

```python
script.get_host()           # -> PyAcadHost. The same object as the __pyacad__ global,
                            #    available even outside a script scope.
script.get_script_path()    # -> str, the executing command's script.py
script.get_command_path()   # -> str, the executing command's bundle folder
script.get_title()          # -> str, the executing command's resolved title
script.get_host_version()   # -> str, pyACAD runtime assembly version
script.is_dark_theme()      # -> bool. Theme your own dialogs with this.
script.reload_pyacad()     # -> None. Rescans extension folders and rebuilds the ribbon.
script.get_toggle_state()   # -> bool. True when THIS *.toggle bundle is currently on.
script.set_toggle_state(on) # -> None. Sets THIS *.toggle bundle's state, flips its icon.

script.get_envvar(name, default=None)   # -> session-scoped value shared by every pyACAD
                                        #    script this session, or default if unset.
script.set_envvar(name, value)          # -> None. value=None removes it. Gone on restart.

script.clipboard_copy(text)             # -> None. Windows clipboard.
script.clipboard_text()                 # -> str. '' when empty or unreadable. NEVER raises:
                                        #    the clipboard is machine-wide, so another app
                                        #    holding it open must not kill a tool that polls it.
script.open_url(url)                    # -> None. Default browser.
script.show_in_explorer(path)           # -> None. Explorer, file selected (or a folder as-is).
script.get_bundle_file(name)            # -> str path of a file beside script.py in the
                                        #    current bundle, or None when it does not exist.

script.get_config(defaults)             # -> settings.load() keyed off the RUNNING bundle's
                                        #    own path ('cfg_' + slugified key). No shared
                                        #    TOOL constant needed between config.py/script.py.
script.save_config(values)              # -> settings.save(), same auto-derived key.
script.reset_config()                   # -> None. Deletes this bundle's saved config file.

script.get_data_file(suffix='json')             # -> str path, shared across documents.
script.store_data(key, value, per_document=False)   # -> None. JSON-serialisable value.
                                                #    per_document=True is not available yet.
script.load_data(key, default=None, per_document=False)   # -> the stored value, or default.
script.data_exists(key, per_document=False)     # -> bool.

script.get_logger()   # -> a Logger for the running code (cached by log label per run):
                      #    .debug/.info/.success/.warning/.error(message), .set_level(level)
                      #    Always writes to the pyACAD log file. warning/error ALSO print a
                      #    colored line to the output window, but only when one is already
                      #    open - a logger call never opens the window by itself.
                      #    Default threshold is 'info': debug is silent until set_level('debug').
                      #    Label is the command's title, or 'hook <ext>:<event>' /
                      #    'startup <ext>' for those (PyAcadHost.LogSource); they set no
                      #    command context, so without it they took the LAST command's title.
```

**TRAP: `get_config`/`save_config` derive their key from the running bundle's own path, so two
bundles can never share one config store.** Reach for `pyacad.settings` with an explicit
`TOOL` constant when two buttons must deliberately agree on one settings file.

**TRAP: outside a run these go stale, not blank.** The host keeps the context of the most
recent run, so from a console or a unit test you get whatever ran last, or `None` if nothing
has. Only trust them inside a command.

**TRAP: a handler that fires AFTER script.py returned is "outside a run".** A modeless
dialog's Click/Closed handler, a dock panel's handler, a timer: by then the user may have run
another tool, and `script.save_config()` / `store_data()` would silently read or overwrite
THAT tool's file. Pin the identity while the command is still running and use the pinned
object in every late callback:

```python
me = script.bind()                       # top of script.py -> BoundScript
def on_closed(sender, args):
    me.save_config({'width': window.Width})
## me.get_config / save_config / reset_config / store_data / load_data / data_exists
```

`pyacad.settings` and `pyacad._datafiles` writes are atomic (temp file, then replace), and
`store_data` over a corrupt data file keeps the old bytes as `<file>.corrupt`.

`reload_pyacad()` rebuilds the ribbon that owns the button currently executing. It is what
the shipped Reload button calls.

**TRAP: `get_toggle_state`/`set_toggle_state` only make sense inside a `*.toggle` bundle's own
run.** Both act on "whichever bundle is currently executing", which is set only by an actual
click of `script.py` or `config.py`. Calling either from a hook, `startup.py`, or a
smartbutton's build-time `__selfinit__` run has no bundle to act on and does not reliably
target any particular toggle. State lives for the AutoCAD session only; a toggle that must
come back on after a restart needs `pyacad.settings`, read from its own `script.py`.

### settings

Import needs nothing beyond the stdlib. Works in any engine and outside AutoCAD entirely.
One JSON file per tool under `%APPDATA%\pyACAD\settings\`.

```python
settings.path_for(tool)              # -> str, '<%APPDATA%>\pyACAD\settings\<tool>.json'
settings.merge(defaults, stored)     # -> dict
settings.load(tool, defaults)        # -> dict
settings.save(tool, values)          # -> None
settings._APPDATA_OVERRIDE           # module-level test seam, points the store elsewhere
```

**`settings.load` NEVER raises.** A missing file, a corrupt file, a non-object JSON body, an
unreadable path: every one of them is caught by a bare `except Exception` and returns
`dict(defaults)`. There is no way to tell "no saved settings" from "the file is broken".

**`settings.merge` drops keys outside `defaults`.** It iterates `defaults`, not `stored`, so
stale entries from an old tool version never leak back in. A key you add to `DEFAULTS` later
simply starts appearing.

**`settings.save` is NOT atomic.** It creates the directory when missing and writes straight to
the target with `json.dump(values, f, indent=2, sort_keys=True)`. An interrupted write leaves a
truncated file, which `load` then treats as absent. Contrast `memory.save`, which writes a
`.tmp` file and `os.replace`s it. `save` propagates `IOError`/`OSError` from the write.

Values must be JSON-serialisable. There is no schema and no validation.

### toast

Import needs `PyAcad.Runtime` only. **Never blocks**, never takes focus, dismisses itself,
opens no window.

```python
toast.show(level, message, detail=None)   # level: 'success' | 'error' | 'info' | 'warning'
toast.success(message, detail=None)       # green
toast.error(message, detail=None)         # red
toast.info(message, detail=None)          # blue
toast.warning(message, detail=None)       # amber
```

`level`, `message` and `detail` are all passed through `str()`; `detail=None` stays `None`.

A toast is one line plus an optional muted detail line. It carries no buttons, so it can never
ask a question, and it cannot hold a table.

### xl

Import needs nothing beyond the stdlib: `zipfile` and `xml.dom.minidom`. Runs unmodified on
IronPython 3.4 and CPython alike, and needs neither Excel nor AutoCAD installed.

```python
xl.write(path, rows, headers=None, sheet='Sheet1')
    # rows: each a list of str/int/float/bool/None.
    # headers, if given, written as a bold first row.
    # -> None. Writing to a path that already holds a workbook ADDS the named sheet, or
    #    REPLACES it if that name already exists; every other sheet keeps its name, position
    #    and cell VALUES only. Implemented by reading the whole workbook back and rebuilding
    #    the package from scratch, which is a VALUES-ONLY round trip: an untouched sheet loses
    #    its bold header row and any int written to it comes back as float.

xl.read(path, sheet=None)
    # sheet=None reads the first sheet.
    # -> [[cell, ...], ...]. Numbers as float, booleans as True/False, everything else
    #    (inline AND shared strings) as str. Row length follows the highest column used;
    #    gaps within that span normalise to ''.
    # RAISES ValueError: no sheets in the workbook, or sheet names one that is not in it.

xl.sheets(path)
    # -> [str], sheet names in workbook order.
```

**TRAP: rewriting a file loses formatting on every sheet except the one you just wrote.**
`write` rebuilds the whole package from the plain values `read` hands back for every sheet it
did not touch this call, so an untouched sheet's previously-bold header row comes back plain,
and an int written to it earlier comes back as a float. Only the sheet named in the current
call gets fresh formatting, from the `headers` you pass it this time. Pass `headers=` on every
write to every sheet that must stay bold, and do not depend on an integer round tripping
through a sheet `write` did not touch this call.
