<p align="center">
  <img src="assets/logo/pyacad-logo.png" alt="pyACAD" height="96">
</p>

# pyACAD

Python scripting and a folder-driven ribbon for Autodesk AutoCAD 2021 to 2026.

Drop a folder with a `script.py` and an icon into an extension, press Reload, and it is a
ribbon button. No compiling, no restart, no plugin boilerplate. If you have used
[pyRevit](https://github.com/pyrevitlabs/pyRevit), the layout will feel familiar: pyACAD
brings the same idea to AutoCAD.

> pyACAD is an independent project and is not affiliated with or endorsed by Autodesk.

> **Status: 0.1.0, the first release.** The ribbon, commands and aliases, hooks, dock panes
> and the bundled tools run in AutoCAD; the automated smoke run covers AutoCAD 2026. Please
> report what breaks in [Issues](https://github.com/pyacad/pyACAD/issues).

## What you get

- **A ribbon built from folders.** `*.pushbutton`, `*.stack`, `*.pulldown`, `*.splitbutton`,
  `*.toggle`, `*.smartbutton`, `*.urlbutton`, `*.linkbutton`, `*.slideout` and more. Rename a
  folder and the button renames; list the folders in a `layout:` and they reorder.
- **Two Python engines.** IronPython 3.4 ships with pyACAD and is the default. Set
  `engine: cpython` in `bundle.yaml` to run a script on your installed CPython when you need
  numpy, pandas or any other compiled package.
- **Live reload.** Edit a script, click the button again. Add a bundle, press Reload.
- **Hooks and startup scripts.** `hooks\doc-opened.py` and 19 other AutoCAD events run with
  no click; `startup.py` runs at boot and on every Reload.
- **Commands and aliases.** Every button is also a command you can type, put in a macro or
  call from AutoLISP, and `alias: BX` adds a short name. An alias never replaces one in
  your `acad.pgp` or an existing command.
- **Keyboard shortcuts and keytips** declared per bundle, with an in-app editor for overrides.
- **Context rules.** `context: selection & modelspace` greys a button out until it can work.
- **The `pyacad` library.** Forms and pickers, toasts, a rich HTML output window with tables
  and charts, per-tool settings and Excel read and write.
- **Light and dark themes**, with a full icon set for both.
- **Ask AI (beta).** A chat panel that writes a tool from a plain-words request, using your
  own Anthropic or OpenAI-compatible key, and revises it when you ask.

## Bundled tools

One tab, `pyACAD`, with eight panels. Every tool is also an AutoCAD command, and the
drawing tools have a short alias to type (in brackets). The
[tools guide](https://tools.pyacad.com) has a page for each.

**pyACAD**

- **Settings**: theme, ribbon hints, extension folders and engine paths
- **Shortcuts**: view and change every keyboard chord and alias
- **Console**: an interactive Python console inside AutoCAD
- **Reload**: rescan the extension folders and rebuild the ribbon, no restart

**Data**

- **Blocks to Excel** (`BX`): block references and their attributes to a workbook
- **Excel to Blocks** (`EXB`): edited attribute values and layers back from that workbook
- **Block Schedule** (`BSC`): counts blocks by name, or name and layer, as a table or to Excel
- **Find and Replace Attributes** (`FRA`): replaces text in attribute values across the drawing

**Layers**

- **Layer Manager**: a docked, filterable layer list to isolate, freeze, lock, merge or delete
- **Layer Mapper** (`LMAP`): applies a layer standard from a CSV, renaming or merging layers
- **Layer Snapshot** (`LSN`): saves which layers are on, frozen and locked, and restores it

**Xrefs**

- **Xref Manager** (`XM`): reload, unload, detach or bind several xrefs at once
- **Xref Repath** (`XRP`): make xref paths relative or absolute, or find and replace in them

**Cleanup**

- **Drawing Cleanup** (`DCU`): purge, audit, zero-length and duplicate geometry in one run
- **Font and Proxy Report** (`FPR`): proxy objects and missing fonts, with an Excel export

**Output**

- **Batch Plot** (`BP`): the layouts you pick to PDF, one file each
- **Layout Manager** (`LYM`): rename, reorder, copy or delete layouts, or apply a page setup

**Takeoff**

- **Length Takeoff** (`LTO`): total length per layer of lines, arcs, polylines and splines
- **Tag Audit** (`TAU`): duplicate, blank and missing values of one attribute across blocks

**AI (beta)**

- **Ask AI**: a chat that writes a pyACAD tool from your request, creates it on an AI tab,
  and revises it; needs your own API key or a local OpenAI-compatible server
- **AI Settings**: provider, endpoint, model and key, stored encrypted outside `config.json`
- **Open AI Folder**: the `AI.extension` folder the generated tools live in

## Requirements

- Windows with Autodesk AutoCAD 2021, 2022, 2023, 2024, 2025 or 2026
- [.NET SDK](https://dotnet.microsoft.com/download) 9.0.200 or later, only to build from
  source, because the solution uses the `.slnx` format. pyACAD itself targets .NET Framework
  4.8 for AutoCAD 2021 to 2024 and .NET 8 for AutoCAD 2025 and 2026.
- Optional: a 64-bit CPython install supported by Python.NET 3.0 (3.7 through 3.13), only for
  bundles that declare `engine: cpython`

## Install

Setup installs for the current user only, so there is no administrator prompt. Close
AutoCAD first. Setup writes into your own profile:

| What | Where |
| --- | --- |
| Loader, one folder per AutoCAD year | `%APPDATA%\Autodesk\ApplicationPlugins\pyACAD.bundle\Contents\<year>\` |
| Runtime, stdlib and the `pyacad` library | `%APPDATA%\pyACAD\<year>\runtime\` |
| The shipped extension | `%APPDATA%\pyACAD\extensions\pyACAD.extension\` |
| Settings and logs | `%APPDATA%\pyACAD\` |

`PackageContents.xml` in the bundle root tells AutoCAD which `Contents\<year>` folder to
load, so one bundle serves every release you have installed.

An update keeps your `config.json`, your logs and every extension of your own. It replaces
`pyACAD.extension` whole, so keep your own tools out of it: put them in
`%APPDATA%\pyACAD\extensions\My pyACAD.extension`, where a `pyACAD.tab` or a panel of the
same name joins the pyACAD one on the ribbon.

pyACAD is provided as is, under the [Apache License 2.0](LICENSE), with no warranty.

## Build from source

The AutoCAD API reference assemblies come from the `AutoCAD.NET` NuGet package, so nothing
needs to be installed or copied by hand. Pick the release with `AcadVersion` (2021 to 2026,
default 2026):

```powershell
git clone https://github.com/pyacad/pyACAD.git
cd pyACAD

dotnet build pyACAD.slnx -p:AcadVersion=2026      # .NET 8 build for AutoCAD 2026
dotnet build pyACAD.slnx -p:AcadVersion=2024      # .NET Framework 4.8 build for AutoCAD 2024
dotnet test src/PyAcad.Tests -p:AcadVersion=2026

# Install for every AutoCAD release that has a build. Close AutoCAD first.
powershell -ExecutionPolicy Bypass -File tools\install.ps1
```

To work on pyACAD itself, use `tools\deploy-dev.ps1` instead: it installs the loader the
same way but points `config.json` at the repository's `bin` folder, so a rebuild is picked up
by the next AutoCAD start without reinstalling anything.

### Releasing

Versions are `MAJOR.MINOR.PATCH`. The one source is `__version__` in
`pyacadlib/pyacad/__init__.py`; the build stamps it into every assembly, the installer
name, the Autodesk manifest and the Settings window. To cut a release, date the version's
entry at the top of [CHANGELOG.md](CHANGELOG.md) as `## [x.y.z] - yyyy-mm-dd`, run the
tests, then `powershell -ExecutionPolicy Bypass -File tools\package.ps1` with AutoCAD closed.

### Websites

The three websites are plain static files in this repo. Cloudflare Pages deploys each
folder from `main` as it is, with no build step there, so every push to `main` is a deploy.
The two guides are generated: edit the fragments in `docs/*/_content/`, never the pages,
then rebuild and commit the output.

```powershell
python tools\build_docs.py            # authoring guide, then lint (0 errors expected)
python tools\build_tools_site.py      # tools guide
python -m unittest discover -s tests/tools
python tools\preview.py               # all three sites at http://localhost:8600/ to 8602/
```

Each builder's `--check` fails when the committed pages are out of date.

## Write your first button

```
MyTools.extension\
  MyTools.tab\
    Demo.panel\
      Hello.pushbutton\
        script.py
        bundle.yaml      (optional)
        icon.png         (optional)
```

```python
# script.py
"""Says hello to whoever clicks it."""
from pyacad import forms, toast

name = forms.ask_string('Your name:', '', 'Hello')

if name is not None:                     # None means cancelled: say nothing
    toast.success('Hello, %s' % (name or 'world'))
```

Add the folder that holds `MyTools.extension` to the `"extensions"` array in
`%APPDATA%\pyACAD\config.json`, press **Reload**, and click.
Shift+Click runs the bundle's `config.py` if it has one; Alt+Click opens the bundle folder.

## Command line

`pyacad.exe` (`env`, `attach`, `detach`, `extensions`, `run`) is installed with pyACAD, in
`%APPDATA%\pyACAD\cli`. `pyacad run` runs a script on one or more drawings in AutoCAD
Core Console, without the AutoCAD window; `pyacad run --help` lists its options.

## Repository layout

| Path | What it is |
|---|---|
| `src/PyAcad` | The thin loader AutoCAD loads |
| `src/PyAcad.Runtime` | Bundle parser, ribbon builder, script engines, forms, output window |
| `src/PyAcad.Cli`, `src/PyAcad.Tests` | Command-line helper and the test suite |
| `pyacadlib/pyacad` | The Python library scripts import |
| `extensions/` | The shipped pyACAD extension |
| `tests/fixtures` | A Smoke extension of worked examples covering every bundle kind (not installed; see its README) |
| `tests/tools` | Checks that the committed websites match their sources |
| `site/` | The homepage, published at [pyacad.com](https://pyacad.com) (hand-written static files) |
| `docs/authoring/` | The tool-authoring guide, published at [docs.pyacad.com](https://docs.pyacad.com) (generated by `tools/build_docs.py`) |
| `docs/tools/` | The tools guide, published at [tools.pyacad.com](https://tools.pyacad.com) (generated by `tools/build_tools_site.py`) |
| `tools/` | Installer, dev deploy script, icon and logo generators, website builders |

## Acknowledgements

pyACAD owes its core idea, its bundle folder conventions and the shape of parts of its
scripting API to [pyRevit](https://github.com/pyrevitlabs/pyRevit) by Ehsan Iran-Nejad and
contributors. pyACAD is an independent implementation and contains no pyRevit source code.

It is built on [IronPython](https://ironpython.net/) and
[Python.NET](https://github.com/pythonnet/pythonnet).

## Licence

[Apache License 2.0](LICENSE). See [NOTICE](NOTICE) for attributions.

Autodesk and AutoCAD are registered trademarks of Autodesk, Inc.
