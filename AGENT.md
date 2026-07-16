# Agent guidelines

Guidance for AI agents working in this repository.

## UI implementation

- **Prefer declarative XAML over imperative C#.** Whenever a feature can be expressed in
  XAML — a control, attached property, style, resource, binding, or template — declare it in
  the relevant `.axaml` file rather than constructing and wiring it up in code-behind or
  `App.axaml.cs`. Reserve C# for what genuinely cannot be expressed declaratively (event
  handlers, app lifetime/threading, dynamic runtime composition), and bind the XAML to view
  models for the rest.
- Example: Avalonia's `TrayIcon` is a control — declare it via the `TrayIcon.Icons`
  attached property in `App.axaml` (see https://docs.avaloniaui.net/controls/navigation/trayicon),
  not with `new TrayIcon(...)` + `TrayIcon.SetIcons(...)` in `App.axaml.cs`.

## Localization

- **Never hardcode user-facing text.** Every display string lives in
  `SyncMaid/Lang/Strings.resx` (neutral English), with full translations in
  `Strings.zh-Hans.resx`, `Strings.zh-Hant.resx`, and `Strings.ja.resx` — keep all four
  files key-identical. Brand strings ("SyncMaid" window title, tray tooltip) and the cron
  placeholder pattern are the only deliberate literals.
- **XAML**: `{l:Loc Some.Key}` with `xmlns:l="using:SyncMaid.Markup"` — a reflection-free
  compiled binding to the `Localizer` singleton that re-renders in place when the language
  switches at runtime.
- **C#**: `Strings.Some_Key` (dots become underscores) for plain strings,
  `Localizer.Format(Strings.Some_KeyFormat, ...)` for composite formats, and
  `Localizer.Plural("Some.Key", count)` for `.One`/`.Other` plural pairs.
- After adding/renaming/removing keys in `Strings.resx`, regenerate the accessor class:
  `powershell -File tools/generate-strings.ps1`.
- Engine (`SyncMaid.Core`) exception messages stay English — localize only the UI wrapper
  sentence around them. Core carries no display strings.
- Tests that call `Localizer.Apply` must be `[AvaloniaFact]` (the change notifies UI-bound
  view models, so it must run on the UI thread) and must restore English in `finally`;
  the test bootstrap pins English and disables test parallelization because the UI culture
  is process-global.

## Task shape conventions

These are product rules, not implementation details: enforce them, don't engineer around
them. Both are validated in the editor (blocked with a hint) **and** in the engine (the
run fails without touching files), so hand-edited config is covered too.

- **A task's source and destinations never nest.** A destination path must not equal the
  task's source path, must not be inside it, and must not contain it — in either
  direction, for every strategy. Sibling folders under a common parent are fine.
  Rationale: a destination inside the source turns the app's own output into input
  (feedback loops); a source inside a destination makes Mirror treat the live source as
  orphaned destination content and delete it. Do **not** add code to make nested layouts
  work (e.g. excluding a nested subtree from planning) — reject the layout instead.
- **Move is exclusive.** A destination with the Move strategy must be the only
  destination of its task: with a Move destination in place, no other destination can be
  added; with any destination in place, a Move destination cannot be added. Rationale:
  Move's postcondition (an emptied source) contradicts every other strategy's
  precondition (the source is the truth), so combinations have no coherent semantics —
  within a run they are order-dependent, and across runs Mirror+Move deadlocks on the
  empty-source guard.
- **Mirror takes no file filters.** A Mirror destination always syncs all files.
  Rationale: Mirror's contract is tree identity — whenever no task is running, a
  file-tree compare of source and destination reports identical, empty directories
  included — and a filtered subset contradicts that by definition. The editor hides
  the filter section for Mirror and persists a lone all-files filter (normalizing
  legacy config on save); the engine refuses a hand-edited Mirror destination whose
  filter list is anything but a lone all-files filter.
- **Tasks never share same-kind paths.** Across tasks, a source may not equal or nest
  with another task's source, and a destination may not equal or nest with another
  task's destination — in either direction. A destination feeding another task's source
  (chaining: task A moves files into a folder task B watches and backs up) is
  explicitly allowed; chained runs converge via trigger coalescing and idempotent
  planning. Rationale: runs of different tasks are concurrent and uncoordinated, so
  overlapping destinations race on the same files (one task's Mirror deletes what
  another just wrote as "orphans") and overlapping sources double-process the same
  input (fatal once one of them is a Move). Enforced in the editors and at run start.

## Commits

- Do **not** add a "Co-Authored-By: Claude" trailer (or any AI co-author/attribution
  trailer) to commit messages.
- Write clear, conventional commit messages describing the change and its intent.
