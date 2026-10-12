# Test fixtures

`Smoke.extension` and `Smoke.lib` exercise every bundle kind, hook and engine seam
pyACAD supports. They live here rather than under `extensions/` on purpose: that
folder is the scanned root, so anything in it becomes a real ribbon tab in every
user's AutoCAD, and a tab full of demo buttons is not something to ship.

To run the smoke pass, point a pyACAD extension root at this folder:

```json
{ "extensions": ["...\pyACAD\extensions", "...\pyACAD\tests\fixtures"] }
```

Reload, work through the checklist, then take the root back out. `Smoke.lib` is a
`*.lib` sibling, so adding or removing it needs an AutoCAD restart rather than a
Reload.
