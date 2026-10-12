"""Builds src/PyAcad.Runtime/Ai/authoring-pack.md, the authoring guide the AI panel
hands to the model as its system prompt and to people as "Copy authoring guide".

Sources, in order:
  1. A hand-written frame below: what pyACAD is and the pushbutton contract in brief.
  2. The maintainers' operational authoring reference (the tool-authoring skill's
     SKILL.md sections on hard rules, pure/API splitting, what needs what, and the
     silent failure modes), which is NOT published on its own.
  3. references/templates.md, references/design-rules.md, references/api.md from the
     same folder.

    python tools/build_authoring_pack.py            regenerate the pack
    python tools/build_authoring_pack.py --check    fail if the pack is out of date

Dev-time CPython only, standard library only. This script is deliberately absent from
tools/publish.py's ALLOW list because it reads a folder the public repo does not carry;
the generated pack IS published, so the scrub below keeps internal tooling names out.
Never hand-edit the pack: change a source and re-run.
"""

from __future__ import annotations

import io
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SKILL = os.path.join(ROOT, '.claude', 'skills', 'pyacad-tool-authoring')
REFS = os.path.join(SKILL, 'references')
OUT = os.path.join(ROOT, 'src', 'PyAcad.Runtime', 'Ai', 'authoring-pack.md')

FRAME = """# Writing pyACAD tools

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

"""

# SKILL.md sections to carry over, by heading. The rest of that file is workflow for a
# coding agent working in this repository and does not apply to a tool the panel writes.
SKILL_SECTIONS = [
    'Hard rules',
    'Splitting pure logic from API calls',
    'What needs what to take effect',
    'Failure modes that are silent',
]

REF_FILES = [
    ('templates.md', 'Templates'),
    ('design-rules.md', 'Design rules'),
    ('api.md', 'The pyacad library'),
]

# Internal-tooling names that must not reach the published pack.
SCRUB = [
    (re.compile(r'`?references/[\w\-]+\.md`?'), 'the sections below'),
    (re.compile(r'\bSKILL\.md\b'), 'this guide'),
    (re.compile(r'\bsuperpowers\b', re.IGNORECASE), ''),
    (re.compile(r'\.claude[\\/][^\s`]*'), ''),
    (re.compile(r'\u2014'), '-'),
]


def read(path: str) -> str:
    with io.open(path, encoding='utf-8') as fh:
        return fh.read()


def skill_sections(text: str) -> str:
    """The chosen `## ` sections of SKILL.md, headings included."""
    parts = re.split(r'(?m)^(?=## )', text)
    wanted = []
    for part in parts:
        heading = part.split('\n', 1)[0].lstrip('#').strip()
        if heading in SKILL_SECTIONS:
            wanted.append(part.rstrip() + '\n')
    missing = [h for h in SKILL_SECTIONS if not any(p.startswith('## ' + h) for p in wanted)]
    if missing:
        sys.exit('SKILL.md is missing section(s): ' + ', '.join(missing))
    return '\n'.join(wanted)


def demote(markdown: str) -> str:
    """Push every heading one level down so the pack has a single top-level title."""
    return re.sub(r'(?m)^(#{1,5}) ', lambda m: '#' + m.group(1) + ' ', markdown)


def scrub(text: str) -> str:
    for pattern, replacement in SCRUB:
        text = pattern.sub(replacement, text)
    return text


def build() -> str:
    parts = [FRAME.rstrip() + '\n']
    parts.append('## Rules and traps\n\n' + demote(skill_sections(read(os.path.join(SKILL, 'SKILL.md')))))
    for name, title in REF_FILES:
        body = read(os.path.join(REFS, name))
        body = re.sub(r'(?m)^# .*\n', '', body, count=1)      # drop the file's own H1
        parts.append('## ' + title + '\n' + demote(body.strip()) + '\n')
    text = scrub('\n'.join(parts))
    text = re.sub(r'\n{3,}', '\n\n', text).rstrip() + '\n'
    for pattern in (r'superpowers', r'\.claude[\\/]', r'CLAUDE\.md', r'SKILL\.md', r'\u2014'):
        if re.search(pattern, text, re.IGNORECASE):
            sys.exit('scrub failed: pack still contains ' + pattern)
    return text


def main() -> int:
    if not os.path.isdir(REFS):
        sys.exit('authoring reference folder not found: ' + REFS)
    text = build()
    check = '--check' in sys.argv[1:]
    current = read(OUT) if os.path.isfile(OUT) else None
    if check:
        if current != text:
            print('authoring-pack.md is out of date; run python tools/build_authoring_pack.py')
            return 1
        print('authoring-pack.md is up to date')
        return 0
    if current == text:
        print('authoring-pack.md unchanged (%d chars)' % len(text))
        return 0
    with io.open(OUT, 'w', encoding='utf-8', newline='\n') as fh:
        fh.write(text)
    print('wrote %s (%d chars)' % (os.path.relpath(OUT, ROOT), len(text)))
    return 0


if __name__ == '__main__':
    sys.exit(main())
