# pyACAD in-app smoke test

Nothing in pyACAD has run inside AutoCAD yet: every slice so far was built and unit-tested
on a machine without AutoCAD. This is the checklist for the first session. Work it top to
bottom; each step assumes the ones above passed. Keep `%APPDATA%\pyACAD\logs\loader.log`
and `pyACAD.log` open in a tail viewer.

## Automated run

`tools\smoke\run-smoke.ps1` works this checklist without a person at the keyboard:

```
powershell -ExecutionPolicy Bypass -File tools\smoke\run-smoke.ps1 -Version 2026
```

It backs up `config.json`, points the extension roots at `extensions`, `tests\fixtures` and a
per-run copy of `tests\smoke\probe-extension`, launches AutoCAD, and drives it through COM
`SendCommand`. Evidence comes from three places: `pyACAD.log`, marker files that the probe
extension's hooks and buttons write, and `results\*.json` from `tests\smoke\probes\*.py`,
which run inside AutoCAD through PYACAD and introspect the ribbon, command registry and
palettes. The shortcut step is the only one that needs real focus: it brings AutoCAD to the
front for a few seconds. Each run leaves `report.md`, `results.json`, screenshots and the
logs in `%TEMP%\pyacad-smoke\<stamp>` (or `-RunRoot`). Steps marked `skip` in the report
(PYACADCONSOLE, Alt+Click, element_link, KeepFocus, the three-failure hook disable, Ctrl+C
in a batch run) are the ones still done by hand from the list below. A licensing dialog at
boot stalls the run; the watchdog screenshots every 30 s into `shots\` until it is dismissed.

## 0. Deploy

1. `dotnet build pyACAD.slnx -p:AcadVersion=<year>` for the installed year.
2. AutoCAD closed, then `powershell -ExecutionPolicy Bypass -File tools\deploy-dev.ps1 -Version <year>`.
3. Confirm `%APPDATA%\Autodesk\ApplicationPlugins\pyACAD.bundle\PackageContents.xml` has one
   `Components` block for the year with `LoadOnAutoCADStartup="True"`.

## 1. Load

- [ ] Start AutoCAD. `loader.log` shows `Loader.Initialize` and no `ReportFatal`.
- [ ] `pyACAD.log` shows the boot banner with the right year (`AutoCAD 2026`, not `version unknown`).
      If it says unknown, `HostInfo.HostVersionString` (Application.Version) needs a look.
- [ ] A `pyACAD` ribbon tab exists with the General panel (Console, Reload, Settings, Shortcuts)
      and the AI panel. If the tab is missing but the log says the ribbon built, the
      `RibbonBootstrap.WhenRibbonReady` timing is wrong for AutoCAD.
- [ ] The log line `Commands registered: N (...)` lists PYCONSOLE, PYRELOAD, PYSETTINGS,
      PYSHORTCUTS and the AI tools.

## 2. Commands (the execution spine)

- [ ] Type `PYCONSOLE` on the command line: the console opens. This proves
      `Internal.Utils.AddCommand` registration and document-context execution.
- [ ] Click Console on the ribbon: same window. The command line echoes `_.PYCONSOLE`
      (CommandSender.Wire) first.
- [ ] Type `PYACAD`, give a full path to a .py file that prints something: output window shows it.
- [ ] `PYACADCONSOLE` shows the diagnostic message box.
- [ ] In the AutoLISP console: `(pyacad "PYCONSOLE")` opens the console and returns nil.
      Then a script with `__result__ = {"n": 3}`: `(pyacad "C:\\path\\r.py")` returns `(("n" 3))`.
- [ ] Shift+Click a button that has config.py (none ship yet: add one to a test bundle) runs it
      through the `...CFG` command. Alt+Click opens Explorer on script.py.
- [ ] Make a selection, then run a command: `pyacad.sel.current()` in the script sees it
      (CommandFlags.UsePickSet).
- [ ] Ctrl+Z after a tool that changed the drawing undoes the whole tool in one step.

## 3. Reload

- [ ] Add a new `*.pushbutton` to the extension, click Reload: the button appears and its
      command is typeable without a restart. Remove it, Reload: the command is gone
      (`Utils.RemoveCommand`).
- [ ] The log shows no `Could not remove command` errors.

## 4. Ribbon survival

- [ ] Switch workspace (Drafting & Annotation to 3D Modeling and back): the pyACAD tab
      comes back by itself. Log line: `Workspace loaded without the pyACAD tabs - rebuilding`.
      If it does not, `RibbonPaletteSet.WorkspaceLoaded` did not fire or fired before the
      tabs vanished; try `TabLoaded` instead.
- [ ] `CUILOAD` of any partial CUIx: same.

## 5. Events and context

- [ ] Add `hooks\doc-activated.py` with a toast, open two drawings and switch: toast each time.
- [ ] Add a `context: selection` bundle: button greys with nothing selected, enables on select
      (250 ms debounce). `context: paperspace` follows the Model/Layout tabs.
- [ ] Add `hooks\object-erased.py`: the boot log shows object events enabled; erase an entity,
      hook runs. Remove the hook, Reload: `ObjectEventsEnabled` false.
- [ ] Save the drawing: a `doc-saved` hook fires after the save (Database.SaveComplete).

## 6. Dock pane

- [ ] Add a `*.dockpane` with a `pane.xaml` holding a TextBox: the ribbon shows a toggle; clicking
      it shows a palette with the XAML; closing the palette unchecks the toggle; typing in the
      TextBox does not go to the command line (KeepFocus).
- [ ] Restart AutoCAD: the palette remembers its size and side (GUID from the bundle key).

## 7. Output window and dialogs

- [ ] `pyacad.output.print_table` renders; an `element_link` click selects the entity
      (Selections.Select under a document lock).
- [ ] `pyacad.forms.alert` is modal to AutoCAD and centered on it (HostInfo.MainWindowHandle).
- [ ] Toasts appear bottom-right of the AutoCAD window.

## 8. Shortcuts

- [ ] Shortcuts editor binds Ctrl+Shift+K to Console; pressing it in the drawing area runs it;
      pressing it while the command line has focus (typing a command) does not.

## 9. Headless (Core Console)

- [ ] `pyacad run C:\path\r.py C:\drawings\a.dwg --timeout 120` from `bin\cli\net48\PyAcad.Cli.exe`:
      the run folder under `%APPDATA%\pyACAD\runs` has request/response/transcript/console
      files and results.json says `ok`. `console-1.txt` shows NETLOAD and PYACADRUN echoed.
- [ ] `--save` after a script that changes the drawing writes the file (status `ok`, not
      `save-failed`); a script that raises leaves the file untouched (`failed`).
- [ ] Two drawings run in two processes; Ctrl+C during the second gives `cancelled`.
- [ ] `pyACAD.log` for the run shows the Core Console boot path (`Core Console: no ribbon`).

## 10. Second year

- [ ] Repeat 0 to 2 on a net48 year (2024 or older) if one is installed: the same bundle holds
      both loaders and AutoCAD picks by series.

## Known unknowns to settle in this session

- Whether AutoCAD 2025 and later load the plugin into the default AssemblyLoadContext, which
  `AssemblyResolver` (AppDomain.AssemblyResolve) assumes.
- Whether `ComponentManager.ItemInitialized` fires before or after `Loader.Initialize` in
  AutoCAD (the ribbon may already exist by then; both paths are handled).
- Whether `(pyacad ...)` from LISP, which runs in application context, needs the document lock
  that `DocumentLocks.Locked` takes in the direct-run fallback; the command route does not.
