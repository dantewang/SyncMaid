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

## Commits

- Do **not** add a "Co-Authored-By: Claude" trailer (or any AI co-author/attribution
  trailer) to commit messages.
- Write clear, conventional commit messages describing the change and its intent.
