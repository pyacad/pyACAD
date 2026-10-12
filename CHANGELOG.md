# Changelog

All notable changes to pyACAD, newest first.

## 0.1.0 (unreleased)

- First release: a bundle-driven Python framework for AutoCAD 2021 to 2026.
- Every pushbutton bundle is an AutoCAD command (`command:` in bundle.yaml or derived from the title), so tools run clicked, typed, from macros and from AutoLISP through `(pyacad ...)`.
- Loader commands PYACAD, PYACADRELOAD and PYACADCONSOLE.
- Dock panes are AutoCAD palettes with a ribbon toggle.
- The ribbon rebuilds itself after a workspace switch or CUIx load.
- Hooks for 20 AutoCAD events; object events only when a hook asks for them.
- Context conditions: doc, selection, modelspace, paperspace, xrefs, blocks, layouts, multi-doc.
- `pyacad run <script.py> [drawings]` runs scripts headless in AutoCAD Core Console, one process per drawing, with `--save`, `--timeout`, `--models` and `--results`, and a results.json per run.
- Drawing API modules `pyacad.doc`, `sel`, `ents`, `layers`, `blocks`, `xrefs`, `layouts`, `plot`, `audit`, `lisp` and `units`, plus `script.get_args()` and `script.get_document_path()`.
