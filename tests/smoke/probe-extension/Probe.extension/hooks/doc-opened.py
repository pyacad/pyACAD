"""Smoke marker hook: records every doc-opened event."""
import os, datetime


def mark(name, detail=""):
    """Append one line to <run>/markers/<name>.log; the orchestrator polls these."""
    run = os.environ.get("PYACAD_SMOKE_RUN")
    if not run:
        return
    folder = os.path.join(run, "markers")
    if not os.path.isdir(folder):
        os.makedirs(folder)
    with open(os.path.join(folder, name + ".log"), "a") as f:
        f.write(datetime.datetime.now().strftime("%H:%M:%S.%f") + "\t" + detail + "\n")


mark("doc-opened", str(__event__.get("command")))
