# Settings

Open **Settings** with the gear in the title bar. Changes apply immediately unless noted.

## Language

**System default** follows Windows. You can also pick English, 简体中文, 繁體中文, or 日本語
directly. The whole window switches over as soon as you choose — no restart.

## Startup

**Start SyncMaid when Windows starts** — opens the main window when you sign in.

SyncMaid registers itself the standard, visible way (a per-user entry Windows shows under
**Task Manager → Startup apps**), so no administrator rights are needed and you can always
see it's there.

If you turn SyncMaid off in Task Manager, the setting reports:

> Startup is turned off for SyncMaid in Windows Task Manager (Startup apps). Turn it back on
> there to allow starting with Windows.

Windows' own switch wins, and SyncMaid deliberately doesn't override it — re-enable it in
Task Manager.

Pair this with **Start minimized** below if you want SyncMaid running from sign-in without a
window appearing.

## Window

**Close to the system tray instead of exiting** — closing the window hides it instead of
quitting, so scheduled and watched tasks keep running. Restore it by clicking the tray icon,
or right-click the icon → **Show main window**. **Exit** in the same menu really quits.

With this off, closing the window exits the app and no automatic syncing happens until you
start it again.

**Start minimized to the system tray** — SyncMaid launches hidden, with only the tray icon.
Tasks run as usual. Takes effect on the next launch.

## Storage

Where SyncMaid keeps your tasks, settings, status and log.

- **App data folder** (default) — `%APPDATA%\SyncMaid`. Per-user, survives moving or
  updating the app, roams with your Windows profile.
- **Next to the app (portable)** — a `Data` folder beside `SyncMaid.exe`, so a copy on a USB
  stick carries its configuration with it and leaves nothing behind on the machine.

The button switches modes: it moves your existing data to the new location and restarts
SyncMaid. Your files are copied and verified before the originals are removed, so an
interrupted switch leaves your configuration intact in one place or the other.

Two things it will tell you rather than do:

- If the target isn't writable — a portable install under `Program Files`, for instance —
  the switch is refused and you stay where you are.
- If the move fails partway, your data is left where it was.

Note that "Start with Windows" points at the app's current location, so if you move a
portable install to a different folder, re-enable it there.

## About

The installed version.

## Where things are on disk

Inside the configuration folder (whichever mode you're in):

| File | What it is |
|---|---|
| `tasks.json` | your tasks and destinations |
| `status.json` | the last result per destination |
| `settings.json` | the settings on this page |
| `*.bak` | the previous version of each of the above, kept automatically |
| `logs\syncmaid.log` | the activity and error log |

In portable mode a `portable.marker` file sits beside `SyncMaid.exe` — that's what tells
SyncMaid to use the `Data` folder. All of these are plain text; back them up by copying the
folder. If you hand-edit `tasks.json`, SyncMaid validates it on load and refuses task layouts
that break the [rules](tasks-and-destinations.md#rules-syncmaid-enforces).
