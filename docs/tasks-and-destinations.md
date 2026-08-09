# Tasks and destinations

A **task** is one source folder plus a trigger. A **destination** is one place that source
goes, with its own strategy and filters. Every task can have several destinations; each is
synced independently and has its own status.

## The task card

Each task is a card in the main window:

- **Name** and the source folder.
- A **trigger badge**: **Manual**, **Watching**, or **Scheduled · `0 2 * * *`**. A
  scheduled task also shows **next run in 2 h** (hover for the exact local time).
- A **health summary** of its destinations: **All synced**, **Partly synced**,
  **2 of 3 failed**, **4 in use**, or **No destinations**.
- Buttons: **Run now** (becomes **Stop** while running), **Add destination**,
  **Edit task**, **Delete task**.

**Run all** at the top runs every task. The sidebar lists your tasks; selecting one scrolls
its card into view.

## Sync strategies

Set per destination, in the destination editor.

### Mirror

> Keep the destination identical — copies new and changed files, deletes extras.

Mirror reproduces the source exactly: new and changed files are copied, files the source no
longer has are removed, and the directory structure is replicated — including empty
directories. When no run is in progress, comparing the two trees shows no difference.

Because that identity is the whole contract, **Mirror takes no file filters**: a filtered
mirror would be identical to nothing in particular. The destination editor hides the filter
section when you choose Mirror.

Deletions are what make Mirror powerful and what make it worth understanding. They go to the
Recycle Bin by default, and a run that would delete an unusual proportion of the destination
stops and asks first — see [Safety](safety.md#mirror-deletions).

### Add-only

> Copy new and changed files; never delete from the destination.

The safe accumulator. Files removed from the source stay in the destination forever, and
nothing SyncMaid does under this strategy can lose data at the destination. Use it for
backups where you'd rather keep too much than too little, and for destinations you also
write to by hand.

### Move

> Move matching files to the destination, removing them from the source.

An inbox-emptier: files that match the filters are moved out of the source. Use it for
"downloads land here, file them there" workflows.

Two consequences worth knowing:

- **A Move destination must be the only destination of its task.** Move empties the source;
  every other strategy treats the source as the truth. Combining them has no sensible
  meaning, so SyncMaid won't let you add a second destination alongside a Move.
- Files another program is holding open are skipped, not failed — the task reports them as
  *in use* and moves them on a later run.

## Rules SyncMaid enforces

These are refused in the editor with an explanation, and re-checked when a run starts, so
they hold even for hand-edited configuration.

**A destination is never inside its own source, and never contains it.**
Sibling folders under a common parent are fine, but nesting is not:
`D:\Photos` → `D:\Photos\Backup` is rejected. A destination inside the source would feed the
app's own output back in as input; a source inside a destination would make Mirror see your
live files as extras to delete.

**Two tasks never share a source, and never share a destination.**
Not the same folder, and not nested in one another either. Tasks run independently and
concurrently, so overlapping destinations would race on the same files and overlapping
sources would process the same files twice.

**Chaining is allowed.** One task's destination *may* be another task's source — task A
moves files into a folder that task B backs up. This is a supported layout: the runs settle
in sequence.

## Editing and deleting

**Edit task** changes the name, source, or trigger; **Edit** on a destination row changes
everything about that destination. Changes take effect from the next run.

Deleting a task or a destination asks for confirmation first and cannot be undone — but it
only removes the task from SyncMaid. **Files already synced are left alone**, at both ends.
