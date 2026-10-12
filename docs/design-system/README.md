# pyACAD design system

Version 2.2: a clean-slate direction, deliberately NOT documenting the
legacy Fluent-glass infra. Modern, minimal, native-feeling on Windows 11, and
hardened against AI-design tells (see `rules/rules.html`, "The slop test").

v2.2 added what the system had been silent about while the code drifted: the data
list (virtualization, search-first, selection strip), control states (disabled,
pressed, focus, empty, busy, error), bulk work (progress, cancel, one undo), the
toast, the shared control vocabulary, and an HTML-output parity card. It also
settled three contradictions: the titlebar is always the real Windows one, popups
are the single exception to "no shadows", and the type scale now lists every size
actually in use.

Direction in one paragraph: neutral grays, hairlines and whitespace carry the
structure; the Windows accent appears in exactly three places (primary action,
selection rail, focus ring); numbers are quiet tabular text, not chips; controls
are the native affordances users already know (checkbox, toggle, segmented,
standard captions); each tool gets one signature moment, the 28px light-weight
"readout" number pair. Solid surfaces only.

- `foundations/` - color, type, layout (spacing, radius, the 32px row), and the
  HTML output window's parity mapping
- `components/` - buttons and their states, the data list, controls (segmented,
  dropdown, toggle, input), numbers and status, surfaces, selection, disclosure,
  the readout, bulk-work progress, toast, window chrome
- `rules/` - the slop test: banned tells and their replacements

Each HTML file is a self-contained preview card (first-line `@dsCard` marker).

Code home: `src/PyAcad.Runtime/Forms/DesignSystem.cs` is the single shared v2
toolkit (Tokens palette, Text/TabularNumber, Primary/Secondary/Quiet/Danger
buttons, GroupFrame, InputField, SlimScroll, FocusRing). Every window and dialog
is built from it: the Settings and Shortcuts windows, the alert/confirm/ask dialogs
(`Dialogs.cs`), the text output window (`OutputWindow.cs`), the HTML output window
(`Html/HtmlPage.cs` CSS + `HtmlOutputWindow.cs`), and the console. The v1
FluentControls glass shell (gradient title, pill caption buttons, acrylic, tint
fills) is retired from all of them; migrated surfaces use the real Windows
titlebar.
