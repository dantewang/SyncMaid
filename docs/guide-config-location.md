# Guide: configurable config/data location (portable mode)

**Status:** implementation guide — not yet implemented
**Depends on:** the Settings dialog (guide-settings-autostart.md) as the entry point.

## Current behavior

Everything the app persists lives in one folder, computed once in
`App.ConfigureServices`:

```
%APPDATA%\SyncMaid   (Environment.SpecialFolder.ApplicationData → per-user roaming AppData)
├─ tasks.json
├─ status.json
├─ tasks.json.bak / status.json.bak
└─ logs\syncmaid.log
```

This is the standard per-user location: writable without elevation, survives moving/
updating the executable, and roams with the user profile. It stays the **default**.

## Goal

Let the user choose where all of that lives, with two options:

- **App data folder (recommended)** — the current `%APPDATA%\SyncMaid`.
- **Next to the app (portable)** — a `Data` folder beside the executable
  (`<exeDir>\Data\...`), for USB-stick / portable installs where everything travels
  together and nothing is left in the user profile.

## The chicken-and-egg problem (the key design point)

The setting that selects the config folder cannot itself live *in* that folder. The clean,
standard portable-app solution is a **marker file beside the executable**, checked at
startup before anything else:

- If `<exeDir>\portable.marker` (or a `<exeDir>\Data` folder) exists → **portable mode**:
  config dir = `<exeDir>\Data`.
- Otherwise → **default mode**: config dir = `%APPDATA%\SyncMaid`.

This keeps portable installs truly self-contained (no AppData footprint) and needs no
bootstrap config. Do **not** store the mode in a file inside AppData — that defeats
portability and reintroduces the ordering problem.

## Design

### Resolver (startup, before DI)

Introduce a tiny `ConfigLocation` resolver that runs first in `ConfigureServices` and
produces the base dir:

```csharp
static string ResolveConfigDir()
{
    var exeDir = Path.GetDirectoryName(Environment.ProcessPath)!;
    var marker = Path.Combine(exeDir, "portable.marker");
    return File.Exists(marker)
        ? Path.Combine(exeDir, "Data")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SyncMaid");
}
```

`configPath`, `statusPath`, `logPath` derive from it exactly as today — so this is a
one-line change at the top of `ConfigureServices` plus the resolver. Everything downstream
(stores, log provider) is already path-injected and needs no change.

### Settings UI

Add a "Storage" section to the Settings dialog (segmented control, mirroring the others):
- **App data folder (recommended)** / **Next to the app (portable)**.
- Show the **resolved absolute path** beneath the choice (read-only), so the user sees
  exactly where files go.
- Switching modes should:
  1. **Check writability** of the target (portable next to a Program Files install will be
     read-only — detect and refuse with a clear message, staying on the current mode).
  2. **Offer to migrate** existing files (`tasks.json`, `status.json`, their `.bak`s, and
     optionally `logs\`) to the new location — copy then verify then remove the originals
     (reuse the atomic-copy discipline; never delete the source until the copy is verified).
  3. Create/delete the `portable.marker` accordingly.
  4. Tell the user a **restart is required** (paths are wired at startup; re-resolving live
     would mean rebuilding the DI graph and re-opening the log file — cleaner to restart).

### Behavior notes

- Autostart (guide-settings-autostart.md) already points the Run key at the current exe
  path, so a portable install that is moved will need autostart re-enabled — note this in
  the storage section, or re-write the Run value on each launch when enabled.
- The log file location moves with the config dir (it already derives from it).

## Test plan

- Resolver: returns AppData path with no marker; returns `<exeDir>\Data` when the marker
  exists (inject exe dir / a filesystem seam so this is unit-testable rather than reading
  the real process path).
- Migration: files copied to the new dir and removed from the old; a read-only target is
  detected and the switch is refused with the originals intact.
- Settings VM: the storage choice reflects the current mode and shows the right path.
- AOT publish stays warning-free (only `System.IO` + `Environment.ProcessPath`).
