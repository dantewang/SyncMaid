# Sync Core Hardening — Design

**Status:** proposal (design only, no code yet)
**Goal:** make the copy / move / delete core *safe by construction* — the destination
must never be left corrupted, truncated, or wrongly deleted, even if the process is
killed, the disk fills, the source blips offline, or a file is transiently locked.
Stated priority: **avoid file loss at all costs.**

---

## 1. Current behaviour (baseline)

The pipeline is well-factored: `enumerate → filter → SyncPlanner.Plan` (pure) →
`SyncApplier.Apply` → `PhysicalFileSystem`. The weaknesses are all in the
*byte-moving* layer and in the plan's delete logic.

| # | Severity | Where | Problem |
|---|----------|-------|---------|
| 1 | 🔴 | `PhysicalFileSystem.CopyFile` (`File.Copy(overwrite:true)`) | Overwrites the good destination in place. Interruption (kill / disk-full / source read error / network drop) leaves a **truncated, corrupt** file and the previous good copy is already gone. No post-copy verification. |
| 2 | 🔴 | `SyncApplier.Apply` (Move = copy then `DeleteFile(source)`) | Source is deleted after an **unverified** copy. A corrupt/partial copy + source delete = **permanent loss**. (`IFileSystem.MoveFile`, atomic on-volume, is never used.) |
| 3 | 🔴 | `SyncPlanner.PlanMirror` | Deletes every destination file not in the filtered set. If the source is briefly unavailable, `EnumerateFiles` returns **empty**, so the plan becomes "delete the entire backup." Deletes are permanent. |
| 4 | 🟠 | `SyncEngine.ExecuteDestination` | One locked file (AV scan, open handle, sharing violation) throws and fails the **whole** destination. No transient retry. |
| 5 | 🟠 | trigger wiring | Watch + Manual (or two watch bursts) can run the same task **concurrently** → two writers on one destination → corruption. No run lock. |
| 6 | 🟡 | `PhysicalFileSystem` | No `Flush(true)` before success (crash can lose buffered bytes); no free-space preflight. |

---

## 2. Verifying file integrity — checksum options

> Answering: *"Is there any other checksum mechanism to verify a file's integrity?"*

There are two **distinct** jobs people conflate. Keep them separate:

- **Change detection** — *should I copy this file at all?* Done before copying, must be
  cheap, runs over every file every sync. Today: `FileStamp` = size + last-write-time.
  This is the right tool for this job (it's what rsync/robocopy default to) and should
  stay. Hashing every file on every scan just to decide whether to copy is far too
  expensive.
- **Copy verification** — *did the bytes I just wrote actually match the source?* Done
  once per file that is actually copied, right before committing it over the
  destination. This is where stronger checksums belong.

### 2.1 The checksum families (for copy verification)

| Mechanism | Type | Speed | Catches | Notes |
|-----------|------|-------|---------|-------|
| **Length only** | trivial | free | truncation / partial write (the #1 corruption mode) | Already implicit; should always run. Misses same-length corruption. |
| **CRC32 / CRC32C** | non-crypto checksum | very fast (CRC32C is SSE4.2 hardware-accelerated) | accidental corruption, bit-flips | 32-bit; tiny collision space but fine for accidental errors. Used by zip, ext4, iSCSI. |
| **xxHash (XXH3 / XXH64 / XXH128)** | non-crypto hash | **fastest** (multi-GB/s, near memcpy) | accidental corruption | Best speed/quality for *integrity* (not security). XXH3/XXH128 recommended. |
| **MD5 / SHA-1** | crypto (broken for security) | moderate | accidental + most tampering | Fine for accidental-corruption detection; avoid if you care about adversarial collisions. |
| **SHA-256** | crypto | moderate (HW-accelerated on modern CPUs via SHA extensions) | accidental + adversarial | Gold standard if you want a content fingerprint you can also store/trust long-term. |

In .NET, all of these are AOT-friendly:
- `System.IO.Hashing` (NuGet) → `Crc32`, `Crc64`, `XxHash32/64/128`, `XxHash3`.
- `System.Security.Cryptography` → `SHA256`, `MD5` (built-in, no package).

### 2.2 Beyond a single checksum — stronger verification strategies

1. **Read-back verification (the real gold standard).** After writing the temp file,
   *re-read it from disk* and compare to the source (streaming, byte-for-byte or via
   hash). A plain in-memory hash of what you *intended* to write can still differ from
   what the disk *actually persisted* (controller/cache/media faults). Read-back is the
   only method that proves the persisted bytes are correct. Cost: one extra full read of
   the copied file.
2. **Hash-on-the-fly.** Compute the source hash *while streaming it into the temp file*
   (one read, no extra pass), then compare against either the destination read-back or a
   recomputed temp hash. Cheaper than a separate source pass.
3. **Filesystem-level integrity (out of our control, worth noting).** **ReFS** maintains
   block checksums (integrity streams) and can self-heal on mirrored storage; **ZFS/Btrfs**
   do the same. NTFS does **not** checksum file data. We can't rely on the FS, so we do
   verification in-app — but on ReFS our work is partly redundant.
4. **Persisted manifest (out of scope).** One *could* store a per-file checksum in a
   manifest to power a later "verify backup" pass that detects *bit-rot* long after the
   copy. We are deliberately **not** doing this: post-sync integrity is not SyncMaid's
   responsibility (see §2.3).

### 2.3 Decision

**Scope boundary:** SyncMaid is responsible for integrity *during the sync process only*.
It is **not** responsible for detecting tampering or bit-rot that happens to a file
*after* it has been successfully synced. That rules out the one job SHA-256 would have
won at — a durable, tamper-resistant stored fingerprint — so cryptographic hashing is out
of scope. xxHash detects every in-transit corruption mode (truncation, write errors,
mangled transfer) just as reliably, and faster.

- **Change detection:** keep `FileStamp` (size + mtime) — unchanged.
- **Copy verification (default, always on):** length check **+** **read-back compare**
  using **xxHash (XXH3 / XXH128)** via `System.IO.Hashing`. Hash the source while
  streaming it into the temp file, then re-read the persisted temp and compare — this
  verifies the bytes the disk actually stored, not just what we intended to write, at
  near-I/O speed.
- **Not doing:** SHA-256 / crypto hashing, and any persisted-checksum "verify backup later"
  feature (see §2.2.4) — both serve post-sync integrity, which is outside SyncMaid's
  responsibility.

---

## 3. Proposed design — "never leave a bad file behind"

Principle: every destructive step is **atomic and verified**, and the safety logic lives
in testable Core code (fault-injectable via the in-memory fake), not buried in
`PhysicalFileSystem`.

### A. Atomic, verified copy  *(fixes #1; transitively #2)*

Replace overwrite-in-place with **temp + verify + atomic rename**:

```
1. write source → sibling temp file:  <dest>.syncmaid-tmp-<rand>   (same directory = same volume)
2. Flush(true) the temp                                            (durability)
3. preserve source last-write-time on temp
4. verify: temp length == source length, then read-back hash compare   (§2.3)
5. atomic commit: File.Move(temp, dest, overwrite:true) / File.Replace  (atomic on-volume rename)
6. on ANY failure: delete the temp, leave the existing dest untouched
```

The existing destination is only ever replaced by a **complete, verified** file. An
interrupted copy leaves a stray temp (cleaned on next run) and the good copy intact.
Same-directory temp guarantees the rename is a metadata-only atomic operation.

### B. Verified move  *(fixes #2)*

- Same-volume source→dest: use the atomic `MoveFile` (`File.Move(overwrite:true)`).
- Cross-volume: atomic-verified **copy (A)**, then assert *dest exists and stamp/hash
  matches source* **before** deleting the source. Source is removed only once the
  destination is proven good.

### C. Mirror delete guardrail + Recycle Bin  *(fixes #3)* — chosen scope

- **Empty/missing-source guard:** if the source root does not exist, or enumeration
  yielded zero files, **do not emit any deletes** (and surface a clear error rather than
  silently wiping the mirror).
- **Mass-delete threshold:** if a single run would delete more than *N%* of the existing
  destination files (configurable; e.g. 50%), abort the deletes for that destination and
  report it as needing confirmation, instead of executing a catastrophic purge.
- **Recycle Bin:** route Mirror deletions to the Windows Recycle Bin instead of a hard
  delete, so anything removed is recoverable.
  - Implementation: `Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Only
    ErrorDialogs, SendToRecycleBin)` (works in .NET on Windows, no native interop) or a
    thin `SHFileOperation`/`IFileOperation` wrapper. Exposed as a new `IFileSystem`
    primitive (`RecycleFile`) so the fake can record it. Non-Windows / non-recyclable
    volumes fall back to hard delete with a logged note.

### D. Transient-error retry  *(fixes #4)*

Wrap each operation in a bounded retry with backoff for **transient** I/O only —
`IOException` (sharing violation / lock), `UnauthorizedAccessException` (often a
momentary AV lock) — e.g. 3 attempts, short exponential backoff. Genuine errors after
retries fail that destination as they do today. Per-file failures could optionally be
collected so one bad file doesn't abort the rest of the destination (design toggle).

### E. Per-task run lock  *(fixes #5)*

Serialize executions of a given task so a watch burst can't overlap a manual run or a
prior run still in flight. Options: a per-task `SemaphoreSlim(1,1)` in the engine/owner,
or "coalesce" semantics (if a run is in progress, mark dirty and re-run once on
completion). Recommend: serialize, with coalesce for watch triggers.

### F. Durability & preflight  *(fixes #6)*

- `Flush(true)` the temp before the atomic rename (step A.2).
- Free-space preflight before a copy: if `available < sourceLength` (+ margin), fail fast
  with a clear error rather than filling the volume mid-write. (`DriveInfo
  .AvailableFreeSpace`.)

---

## 4. Required surface changes

**`IFileSystem`** — add small, testable primitives so the safe-copy algorithm lives in
Core:
- stream access or temp-write + atomic-replace: e.g. `OpenRead(path)` / `OpenWrite(path)`
  + `Replace(tempPath, destPath)`, **or** a single `CommitFromTemp(tempPath, destPath)`.
- `RecycleFile(path)` (Recycle Bin delete).
- `long GetAvailableFreeSpace(path)`.
- hashing helper or stream access sufficient to hash (prefer streaming over
  `ReadAllBytes` for large files — see §6).

**`SyncApplier`** — becomes the home of the safe-copy / verified-move orchestration
(temp → flush → verify → commit → cleanup; move = copy+verify+delete or atomic move).

**`SyncPlanner` / engine** — mirror guardrail (empty-source + mass-delete threshold);
deletes carry a "use recycle bin" flag.

**Model / settings** — per-destination knobs: delete-mode (recycle / hard), mass-delete
threshold, retry count. (Copy verification is **always on** with xxHash — not a toggle.)
Persisted via the existing source-gen JSON context.

**In-memory fake (tests)** — implement the new primitives **with fault injection**
(throw at write step, at commit step, corrupt the temp, make source vanish) so we can
assert the safety properties.

---

## 5. Test plan (the proof the goal is met)

Property-style tests against the fake:
- **Interrupted copy → destination unchanged.** Fault at temp-write/commit; assert the
  pre-existing destination still has its original bytes and a stray temp may exist but is
  cleaned next run.
- **Corrupt copy → not committed, source not deleted.** Make read-back verification fail;
  assert dest untouched and (for Move) source still present.
- **Empty/missing source → Mirror emits no deletes.**
- **Mass-delete over threshold → aborted, reported, nothing deleted.**
- **Mirror delete → goes to Recycle Bin** (fake records `RecycleFile`, not hard delete).
- **Transient lock → retried then succeeds; permanent error → fails that destination.**
- **Concurrent runs of one task are serialized** (no interleaved writes).
- **Free-space preflight → fails before writing when space is insufficient.**
- Existing 103 tests stay green; AOT publish stays warning-free.

---

## 6. Notes / trade-offs

- **Memory:** current `ReadAllBytes`/`WriteAllBytes` load whole files into memory — fine
  for small files, bad for large ones and at odds with read-back verification. The
  hardened copy should **stream** (buffered `Stream` copy + incremental hash), so verify
  cost is bounded and large files don't blow the heap.
- **Performance:** read-back doubles read I/O on copied files only (not on skipped ones).
  xxHash keeps the CPU cost negligible, so verification is effectively disk-bound.
- **ReFS:** our verification overlaps the filesystem's own checksums; acceptable, and we
  can't assume ReFS.
- **Atomicity scope:** the rename is atomic *per file*. A whole-task "all or nothing"
  transaction is out of scope (and rarely wanted for sync); per-file atomicity already
  guarantees no file is ever in a half-written state.
- **Long paths / reparse points:** enumeration follows reparse points today (possible
  loops); worth a follow-up guard, tracked separately from this safety pass.

---

## 7. Suggested sequencing (when we implement)

1. `FileStamp`/change-detection unchanged; add streaming + atomic-commit primitives to
   `IFileSystem` + fake (with fault injection).
2. Atomic verified copy in `SyncApplier` (A, F) + tests.
3. Verified move (B) + tests.
4. Mirror guardrail + Recycle Bin (C) + tests.
5. Transient retry (D) + tests.
6. Per-task run lock (E) + tests.
7. Per-destination settings + JSON persistence; wire into the editor UI.
