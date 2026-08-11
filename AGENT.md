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
- **Pick the right dialog host.** In-window `DialogHost` modals are for flows the user
  starts from the visible main window (editors, delete confirms). Anything that can appear
  while the app is hidden in the tray — the mirror-delete confirmation — must be an
  independent, owner-less top-level window, or nobody sees it.
- The title bar is drawn by us: `ExtendClientAreaChromeHints` is gone in Avalonia 12
  (AVLN2000). Native drag / snap layouts / system menu survive only through
  `WindowDecorationProperties.ElementRole`.

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

## Sync safety

Stated priority: **avoid file loss at all costs.** These are invariants, not defaults —
never add a faster path that skips them.

- **Every write is temp → verify → atomic rename** (`SafeFileTransfer`). Nothing overwrites
  a destination file in place, and Move deletes the source only once the destination
  verifies. Byte-moving code goes through it, not `IFileSystem` directly.
- **Mirror deletions pass `MirrorGuard` first.** An empty or unavailable source emits zero
  deletes and is *not* overridable; a mass delete needs one-shot user confirmation. Deletes
  go to the Recycle Bin by default (`DeleteMode.Recycle`).
- **Safety logic lives in UI-free Core** so the in-memory filesystem can fault-inject it —
  new safety behaviour ships with a fault-injection test.
- **A run never re-triggers itself into a loop.** Runs of a task are serialized by
  `TaskNodeViewModel`'s run gate: while one is active, further requests coalesce into a
  single follow-up rather than starting a second writer. The trigger source stays **live**
  across a run — nothing calls `ITriggerSource.Stop()` — so a run that mutates its own
  source (only Move does) fires the trigger once afterwards. That follow-up is a no-op:
  planning is idempotent, so it changes nothing and the trigger goes quiet again. Do not
  "fix" it by suppressing the trigger around runs without re-reading
  `SelfTriggeringRunTests`, which pins the cost at exactly one extra run and no cascade.
  Notifications deliver outside the owner's state gate (`TriggerNotifier`) — a subscriber
  must never block a watcher callback, and a source's own I/O (the polling walk) must not
  hold that gate either, or `Stop`/`Dispose` block behind a dead network share.

## Persistence & AOT

- **Native AOT is a hard constraint** (`IsAotCompatible` + warnings-as-errors in Core,
  `PublishAot` in the app). No reflection-based serialization: persisted types are
  registered in the source-generated `TaskStoreJsonContext`, and polymorphic models are
  closed hierarchies with string `[JsonDerivedType]` discriminators (`FilterRule`,
  `Trigger`, `DestinationLocation`).
- **Config writes go through `AtomicFile` / `JsonConfigFile`** — temp → rename, previous
  version kept as `.bak` and loaded as a fallback. Never write over `tasks.json` in place.
- **Old config keeps loading.** Normalize legacy shapes on save; never silently discard
  what the current editor can't represent.

## Platform-specific services

- Mark the implementation `[SupportedOSPlatform("windows")]` rather than guarding inside it,
  select it in the composition root via `OperatingSystem.IsWindows() ? … : …`, and provide a
  no-op fallback so callers never branch. That ternary is the shape the CA1416 analyzer
  recognizes, which is what keeps the AOT build warning-free.

## Errors

- **Never swallow an exception.** No empty `catch`; log through `ILogger` and surface the
  failure where the user is — trigger failures as the card's trigger-error badge, per-file
  failures as `SyncOperationException` (prefixed with path + verb). Cancellation propagates
  untouched.

## Commits

- Do **not** add a "Co-Authored-By: Claude" trailer (or any AI co-author/attribution
  trailer) to commit messages.
- Write clear, conventional commit messages describing the change and its intent.
