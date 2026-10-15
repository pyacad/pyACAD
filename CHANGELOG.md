# Changelog

All notable changes to pyACAD, newest first.

## [0.1.0] - 2026-10-15

- First release: a bundle-driven Python framework for AutoCAD 2021 to 2026.
- 15 drawing tools in six panels on the pyACAD tab: Data (Blocks to Excel, Excel to Blocks, Block Schedule, Find and Replace Attributes), Layers (Layer Manager, Layer Mapper, Layer Snapshot), Xrefs (Xref Manager, Xref Repath), Cleanup (Drawing Cleanup, Font and Proxy Report), Output (Batch Plot, Layout Manager) and Takeoff (Length Takeoff, Tag Audit).
- Command aliases: `alias: BX` in bundle.yaml adds a short name to type, users set their own in config.json, and an alias never takes a name acad.pgp or AutoCAD already uses. Each drawing tool ships one.
- The Shortcuts and Aliases editor sets keyboard chords and aliases per tool, and button tooltips name the command and alias to type.
- A click, chord or link button ends a running command before it starts its own.
- Every pushbutton bundle is an AutoCAD command (`command:` in bundle.yaml or derived from the title), so tools run clicked, typed, from macros and from AutoLISP through `(pyacad ...)`.
- Loader commands PYACAD, PYACADRELOAD and PYACADCONSOLE.
- Dock panes are AutoCAD palettes with a ribbon toggle.
- The ribbon rebuilds itself after a workspace switch or CUIx load.
- Hooks for 20 AutoCAD events; object events only when a hook asks for them.
- Context conditions: doc, selection, modelspace, paperspace, xrefs, blocks, layouts, multi-doc.
- `pyacad run <script.py> [drawings]` runs scripts headless in AutoCAD Core Console, one process per drawing, with `--save`, `--timeout`, `--models` and `--results`, and a results.json per run.
- Drawing API modules `pyacad.doc`, `sel`, `ents`, `layers`, `blocks`, `xrefs`, `layouts`, `plot`, `audit`, `lisp` and `units`, plus `script.get_args()` and `script.get_document_path()`.
