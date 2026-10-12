# Probe.extension

The automated smoke suite's own extension (`tools/smoke/run-smoke.ps1`). It is copied
into each run folder and added to the extension roots for that run only, so the shipped
extension and `tests/fixtures/Smoke.extension` stay untouched. Every hook and button
writes a line to `<run>/markers/<name>.log`, which the orchestrator polls, and the
Reload step drops a temporary bundle into the copy. It carries no icons on purpose:
the fallback (no image) path is part of what the run checks.
