# Sync Core — Location, Verification & Safety Design

**Status:** proposal (design only, no code yet)
**Scope:** the single authoritative design for SyncMaid's sync core — *where* files come
from and go to (source/destination abstraction), *how a destination is verified*, and *how
copy / move / delete are made safe by construction*. (This supersedes the earlier separate
"sync hardening" note; everything from it is folded in here.)

**Goal:** the destination must never be left corrupted, truncated, or wrongly deleted —
even if the process is killed, the disk fills, the source blips offline, or a file is
transiently locked — **and** the backend must be able to grow beyond "local → local"
without rewriting the engine. Stated priority: **avoid file loss at all costs.**

**Scope boundary:** SyncMaid owns integrity *during the sync process only* — not tampering
or bit-rot that happens to a file *after* it has been successfully synced (§5.2 explains
why this rules out cryptographic hashing).

---

## 1. Current behaviour (baseline)

The pipeline is well-factored: `enumerate → filter → SyncPlanner.Plan` (pure) →
`SyncApplier.Apply` → `PhysicalFileSystem`. The weaknesses are in the *byte-moving* layer,
the delete logic, and the fact that a single `IFileSystem` is assumed to span both ends of
every transfer.

| # | Severity | Where | Problem |
|---|----------|-------|---------|
| 1 | 🔴 | `PhysicalFileSystem.CopyFile` (`File.Copy(overwrite:true)`) | Overwrites the good destination in place. Interruption (kill / disk-full / source read error / network drop) leaves a **truncated, corrupt** file and the previous good copy is already gone. No post-copy verification. |
| 2 | 🔴 | `SyncApplier.Apply` (Move = copy then `DeleteFile(source)`) | Source is deleted after an **unverified** copy. A corrupt/partial copy + source delete = **permanent loss**. (`IFileSystem.MoveFile`, atomic on-volume, is never used.) |
| 3 | 🔴 | `SyncPlanner.PlanMirror` | Deletes every destination file not in the filtered set. If the source is briefly unavailable, `EnumerateFiles` returns **empty**, so the plan becomes "delete the entire backup." Deletes are permanent. |
| 4 | 🟠 | `SyncEngine.ExecuteDestination` | One locked file (AV scan, open handle, sharing violation) throws and fails the **whole** destination. No transient retry. |
| 5 | 🟠 | trigger wiring | Watch + Manual (or two watch bursts) can run the same task **concurrently** → two writers on one destination → corruption. No run lock. |
| 6 | 🟠 | `SyncEngine` / `IFileSystem` | `CopyFile(src, dst)` reads *and* writes in one store, so a destination can only ever be the same filesystem as the source — no path for cloud/other backends. |
| 7 | 🟡 | `PhysicalFileSystem` | No `Flush(true)` before success (crash can lose buffered bytes); no free-space preflight. |

---

## 2. Source & destination — modelled differently

The source and destination are **not** symmetric, by deliberate decision:

- **Source** is always a **path** the user picks from the Windows file chooser: a local
  directory or a **pre-mounted** network path (mapped drive / UNC). Nothing else is a
  source. The source side stays uniform and keeps using the existing `IFileSystem` for
  enumeration and reads.
- **Destination** is **polymorphic**: a local/mounted path *or* (roadmap) a cloud bucket or
  an SFTP server — each with its own commit and verification strategy.

**Priority:** the **local/mounted path** destination is **phase 1** and the focus of this
doc. Cloud (S3-compatible) and SFTP are **lower-priority roadmap** (§11), specified only
enough to prove the abstraction holds and to record what verification each allows.

### 2.1 Source model — always a path

`SyncTask.SourcePath` stays a filesystem path. A local dir and a mounted network path are
**indistinguishable as paths**; the only difference is *capabilities*, detected at runtime,
not typed:

```csharp
bool IsNetworkPath(string path) =>
    path.StartsWith(@"\\") || new DriveInfo(Path.GetPathRoot(path)!).DriveType == DriveType.Network;
```

### 2.2 Destination model — polymorphic location

A closed, JSON-discriminated hierarchy, mirroring the existing `FilterRule` / `Trigger`
pattern (AOT-safe, no reflection):

```csharp
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(LocalDestination), "local")]      // phase 1 — local OR mounted path
// [JsonDerivedType(typeof(S3Destination),  "s3")]        // roadmap
// [JsonDerivedType(typeof(SftpDestination),"sftp")]      // roadmap
public abstract record DestinationLocation;

public sealed record LocalDestination(string Path) : DestinationLocation;
```

`Destination.Path` (a bare string today) becomes `Destination.Target : DestinationLocation`.
In phase 1 the only kind is `LocalDestination`, covering both local and mounted paths
(network behaviour is a runtime capability, §7.6).

---

## 3. Architecture — the structural core

### 3.1 The one contract that must change

The blocker (baseline #6) is `IFileSystem.CopyFile(src, dst)` — it reads *and* writes in a
single store. To let a destination be anything else, the transfer must become **pull from
source → push to destination**, with the **destination provider owning its own commit +
verification**.

### 3.2 Destination provider

```csharp
public interface IDestinationProvider
{
    DestinationCapabilities Capabilities { get; }

    // Read-side of the destination, for change-detection and Mirror deletes.
    IEnumerable<string> Enumerate(DestinationLocation root);
    bool Exists(DestinationLocation root, string relativePath);
    FileFingerprint? GetFingerprint(DestinationLocation root, string relativePath);

    // Write a source file to the destination. The provider performs its own commit
    // (atomic where possible) AND its own verification (§5), then returns the outcome.
    // It is handed an ISourceFile, not raw bytes, so it can pick the most efficient path.
    WriteResult Write(DestinationLocation root, string relativePath, ISourceFile source, VerifyMode verify);

    void Delete(DestinationLocation root, string relativePath, DeleteMode mode);
}

// Lets a path-destination use the OS fast path (File.Copy / copy-offload) while a
// network/cloud destination streams. In phase 1 LocalPath is always present
// (source is always a path).
public interface ISourceFile
{
    string? LocalPath { get; }   // non-null when the source is a local/mounted path
    long Length { get; }
    Stream OpenRead();           // always available
    FileFingerprint SourceFingerprint { get; }  // size + xxHash, computed once
}
```

A small **registry** resolves the provider for a location's kind — the same
factory-by-kind seam already used for triggers:

```csharp
public interface IDestinationProviderRegistry { IDestinationProvider For(DestinationLocation location); }
```

### 3.3 Engine changes

- Resolve the **source** via the existing `IFileSystem` and **each destination** via
  `registry.For(destination.Target)`.
- `SyncPlanner` is handed the source filesystem **and** the destination provider (instead
  of one `IFileSystem`) so it can enumerate/stamp each side independently. Its pure
  planning logic is otherwise unchanged.
- `SyncApplier` no longer calls `CopyFile`; for a copy it builds an `ISourceFile` and calls
  `destProvider.Write(...)`, letting the provider commit + verify.

### 3.4 Why this is evolutionary, not a rewrite

The closed-hierarchy + JSON-discriminator pattern and the DI factory seam already exist in
the codebase. `PhysicalFileSystem` becomes the read-side of the source **and** the engine
of the `LocalDestination` provider. No new infrastructure style is introduced.

### 3.5 Async note

Path/mounted I/O is synchronous, so phase 1 can keep the contract synchronous. Cloud and
SFTP are inherently async/streamed/paged. To avoid a painful sync→async retrofit later, the
provider methods **should be defined async-ready** (`Task`/`ValueTask`, `IAsyncEnumerable`)
from the start, with the local provider implementing them over synchronous `FileStream`.
(Decision point flagged for implementation — acceptable to defer if we accept the churn.)

---

## 4. Safe data movement — "never leave a bad file behind"

Principle: every destructive step is **atomic and verified**, and the safety logic lives in
testable Core code (fault-injectable via the in-memory fake), not buried in
`PhysicalFileSystem`. These mechanics are the **`LocalDestination` provider's** strategy
(§7); other providers implement their own commit (§11).

### A. Atomic, verified copy *(fixes #1; transitively #2)*

Replace overwrite-in-place with **temp + verify + atomic rename**:

```
1. copy source → sibling temp file:  <dest>.syncmaid-tmp-<rand>   (same directory = same volume)
2. Flush(true) the temp                                           (durability)
3. preserve source last-write-time on temp
4. verify: temp length == source length, then (if enabled) read-back xxHash compare  (§5)
5. atomic commit: File.Move(temp, dest, overwrite:true) / File.Replace   (atomic on-volume rename)
6. on ANY failure: delete the temp, leave the existing dest untouched
```

The existing destination is only ever replaced by a **complete, verified** file. An
interrupted copy leaves a stray temp (cleaned next run) and the good copy intact. The
same-directory temp guarantees the rename is a metadata-only atomic operation. For a
path→path copy the provider may use OS `File.Copy` to the temp (keeping copy-offload/ODX)
via `ISourceFile.LocalPath`, instead of manual streaming.

### B. Verified move *(fixes #2)*

- Same-volume source→dest: use the atomic `MoveFile` (`File.Move(overwrite:true)`).
- Cross-volume: atomic-verified **copy (A)**, then assert *dest exists and stamp/hash
  matches source* **before** deleting the source. Source is removed only once the
  destination is proven good.

### C. Mirror delete guardrail + Recycle Bin *(fixes #3)*

- **Empty/missing-source guard:** if the source root does not exist, or enumeration yielded
  zero files, **do not emit any deletes** (surface a clear error rather than silently wiping
  the mirror).
- **Mass-delete threshold:** if a single run would delete more than *N%* of the existing
  destination files (configurable; e.g. 50%), abort the deletes for that destination and
  report it as needing confirmation, instead of a catastrophic purge.
- **Recycle Bin:** route Mirror deletions to the Windows Recycle Bin instead of a hard
  delete, so anything removed is recoverable.
  - Implementation: `Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
    OnlyErrorDialogs, SendToRecycleBin)` (works in .NET on Windows, no native interop) or a
    thin `SHFileOperation`/`IFileOperation` wrapper. Exposed as a provider capability so the
    fake can record it. **Network shares have no Recycle Bin → falls back to hard delete**
    with a logged note (§7.6).

### D. Transient-error retry *(fixes #4)*

Wrap each operation in a bounded retry with backoff for **transient** I/O only —
`IOException` (sharing violation / lock), `UnauthorizedAccessException` (often a momentary
AV lock) — e.g. 3 attempts, short exponential backoff. Genuine errors after retries fail
that destination as today. Per-file failures could optionally be collected so one bad file
doesn't abort the rest of the destination (design toggle).

### E. Per-task run lock *(fixes #5)*

Serialize executions of a given task so a watch burst can't overlap a manual run or a prior
run still in flight. A per-task `SemaphoreSlim(1,1)`, or "coalesce" semantics (if a run is
in progress, mark dirty and re-run once on completion). Recommend: serialize, with coalesce
for watch triggers.

### F. Durability & preflight *(fixes #7)*

- `Flush(true)` the temp before the atomic rename (step A.2).
- Free-space preflight before a copy: if `available < sourceLength` (+ margin), fail fast
  with a clear error rather than filling the volume mid-write (`DriveInfo.AvailableFreeSpace`).

---

## 5. Verification model

Two distinct jobs, kept separate:

- **Change detection** — *should I copy at all?* Cheap, runs over every file every sync.
  Stays `FileFingerprint` = size + mtime for path destinations; other backends substitute
  their native validator (ETag/checksum/version). Hashing every file every scan just to
  decide whether to copy is far too expensive — this is what rsync/robocopy default to.
- **Copy verification** — *did the bytes land correctly?* Runs once per copied file. This is
  what varies by destination type (§5.4).

### 5.1 Checksum families (for copy verification)

| Mechanism | Type | Speed | Catches | Notes |
|-----------|------|-------|---------|-------|
| **Length only** | trivial | free | truncation / partial write (the #1 mode) | Always run. Misses same-length corruption. |
| **CRC32 / CRC32C** | non-crypto checksum | very fast (CRC32C is SSE4.2 HW-accelerated) | accidental corruption, bit-flips | 32-bit; fine for accidental errors. |
| **xxHash (XXH3 / XXH64 / XXH128)** | non-crypto hash | **fastest** (near memcpy) | accidental corruption | Best speed/quality for *integrity* (not security). **Chosen.** |
| **MD5 / SHA-1** | crypto (broken for security) | moderate | accidental + most tampering | Fine for accidental detection; avoid for new use. |
| **SHA-256** | crypto | moderate (HW-accelerated via SHA-NI) | accidental + adversarial | Only wins at a durable, tamper-resistant *stored* fingerprint — out of scope (§5.2). |

In .NET, all AOT-friendly: `System.IO.Hashing` (NuGet) → `Crc32`, `XxHash3/64/128`;
`System.Security.Cryptography` → `SHA256`, `MD5` (built-in).

### 5.2 Decision: xxHash, no crypto

SyncMaid is responsible for integrity *during the sync only*, not for detecting tampering or
bit-rot *after* a successful sync. That rules out the one job SHA-256 would win at — a
durable, tamper-resistant stored fingerprint — so **cryptographic hashing is out of scope**.
**xxHash (XXH3/XXH128)** detects every in-transit corruption mode (truncation, write errors,
mangled transfer) just as reliably, and faster. A persisted-checksum "verify backup later"
manifest is likewise out of scope (it serves post-sync integrity).

### 5.3 What verification actually guards against, and what transport integrity does *not* cover

Content verification guards against **silent hardware / environmental corruption** — bad
RAM, flaky cables/controllers, in-network bit-flips that TCP's weak 16-bit checksum misses,
torn writes. It is **not** redundant with SMB3 / TLS / SSH integrity. Those MACs are
computed in one protocol stack and verified in the other, so they protect **only the wire
segment between the two stacks** (strong there — far better than TCP's checksum). They do
**not** cover:

- the **client buffer before** the protocol stack — a local RAM flip is signed/encrypted and
  delivered as "valid" garbage; an on-the-fly source hash computed from that same buffer
  agrees with the corruption, so it can't catch this either;
- **server RAM / controller / disk after** the stack verifies — where NAS-side corruption
  actually happens; or
- **data at rest** (out of scope).

The only software method that exercises the uncovered server-side segment is a **read-back
from the destination, compared against the source hash** — which is exactly why content
verification is worth offering, and why it is not made redundant by an encrypted/signed
transport.

> **Residual limit:** a flip in **source** RAM that is hashed consistently can't be caught
> by any software method (no independent reference). That's what **ECC RAM** is for, plus a
> **checksumming filesystem** (ZFS / Btrfs / ReFS) on the server for at-rest protection.
> These are the user's environment to provide; SyncMaid can't guarantee them.

### 5.4 Two tiers of copy verification

- **Basic (always on, cheap):** write-through flush + atomic commit + a post-write
  **length/size** check (a metadata call, not a re-read). Catches truncation / partial
  writes at near-zero cost.
- **Content (opt-in, per destination): xxHash (XXH3/XXH128).** Verifies the actual bytes.
  *How* depends on the destination type, because the cheap server-side option only exists on
  some backends:

| Destination | Basic | Content (xxHash) method | Notes |
|-------------|-------|--------------------------|-------|
| **Local path** | size check + atomic rename | read temp back from local disk, compare | cheap; recommend ON |
| **Mounted network path** | size check + atomic rename | **read-back over the network**, cache-bypassed | ⚠️ warn: doubles network I/O; this is the silent-corruption guard |
| **S3-compatible** (roadmap) | size / ETag | **provider-side checksum** (CRC32C / SHA-256 / MD5), no download | server validates on receipt |
| **SFTP** (roadmap) | size + transport MAC | `check-file` ext if offered, else **download-back** | see §11.3 |

---

## 6. Required surface changes

- **`IFileSystem` (source read-side + shared primitives):** stream access + temp-write +
  atomic-replace — `OpenRead(path)` / `OpenWrite(path)` + `Replace(temp, dest)` (or a single
  `CommitFromTemp`); `long GetAvailableFreeSpace(path)`; streaming hash helper (prefer
  streaming over `ReadAllBytes` for large files — §10).
- **`IDestinationProvider` + registry (§3.2):** the new write-side abstraction; `RecycleFile`
  / delete-mode and capabilities live here.
- **`SyncApplier`:** orchestrates via `provider.Write(...)` (temp → flush → verify → commit
  → cleanup; move = copy+verify+delete or atomic move) instead of `CopyFile`.
- **`SyncPlanner` / engine:** take source-fs + dest-provider; mirror guardrail (empty-source
  + mass-delete threshold); deletes carry a delete-mode.
- **Model / settings:** `Destination.Target : DestinationLocation`; per-destination knobs —
  content-verify toggle, delete-mode (recycle / hard), mass-delete threshold, retry count.
  Persisted via the existing source-gen JSON context.
- **In-memory fake (tests):** implement the new primitives **with fault injection** (throw
  at write step, at commit step, corrupt the temp, make source vanish) to assert the safety
  properties.

---

## 7. Phase 1 — local & mounted path destinations (the focus)

`LocalDestination` covers both; the difference is a runtime capability profile.

### 7.1 Commit + basic verification (always)
The atomic copy of §4.A (temp → `WriteThrough` → flush → length check → atomic rename →
cleanup), using OS `File.Copy` to the temp for the path→path fast path.

### 7.2 Verified move
Per §4.B — atomic on-volume move, or copy-verify-then-delete across volumes.

### 7.3 Content verification (opt-in xxHash)
A **per-destination toggle** ("Verify file contents"). When on, after the temp is written
the provider read-backs it and compares its xxHash to `SourceFingerprint` before committing.
- **Local fixed drive:** read-back is local disk — cheap. Recommended default **ON**.
- **Mounted network path:** read-back is a **full re-read over the network**, and to mean
  anything must **bypass the SMB client cache** (else it re-hashes cached bytes and proves
  nothing). So default **OFF**, and when enabled on a destination detected as network, the
  editor **warns**: *"This is a network location. Content verification re-reads every file
  over the network (slower, more bandwidth). It guards against silent/hardware corruption
  that the network protocol does not."*

### 7.4 Mirror delete guardrail + Recycle Bin
Per §4.C. On a network share, recycle is unavailable → hard delete (§7.6).

### 7.5 Transient retry / run lock / preflight
Per §4.D–F.

### 7.6 Capability degradations on mounted paths
Detected at runtime, surfaced in the UI:
- **No Recycle Bin** on network shares → "recycle on delete" **degrades to hard delete**;
  provider reports `CanRecycle = false`.
- **Watch trigger unreliable over UNC/mapped paths** → prefer a polling watcher for network
  sources (tracked with the trigger work).
- **Free-space / atomicity** behave but with network caveats (free-space needs
  `GetDiskFreeSpaceEx` on a raw UNC path).

| Capability | Local fixed | Mounted network |
|------------|-------------|-----------------|
| Atomic rename commit | ✅ | ✅ (server-dependent) |
| Settable mtime (change-detection) | ✅ | ✅ |
| Recycle-bin delete | ✅ | ❌ → hard delete |
| Content verify | read-back (cheap) | read-back (network, cache-bypass) — warn |
| Wire integrity | n/a | SMB3 sign/encrypt — *wire segment only* (§5.3) |

### 7.7 Secrets
Phase 1 needs **none** — local and pre-mounted paths use ambient OS authentication.

---

## 8. Test plan (the proof the goal is met)

Property-style tests against the fault-injecting fake:
- **Interrupted copy → destination unchanged.** Fault at temp-write/commit; pre-existing
  destination keeps its original bytes; a stray temp may exist but is cleaned next run.
- **Corrupt copy → not committed, source not deleted.** Make read-back verification fail;
  dest untouched and (for Move) source still present.
- **Empty/missing source → Mirror emits no deletes.**
- **Mass-delete over threshold → aborted, reported, nothing deleted.**
- **Mirror delete → Recycle Bin** (fake records recycle, not hard delete); **network
  capability → falls back to hard delete.**
- **Transient lock → retried then succeeds; permanent error → fails that destination.**
- **Concurrent runs of one task are serialized** (no interleaved writes).
- **Free-space preflight → fails before writing when space is insufficient.**
- **Content-verify toggle:** off → no read-back; on (network) → read-back happens.
- Existing 103 tests stay green; AOT publish stays warning-free.

---

## 9. Notes / trade-offs

- **Memory:** current `ReadAllBytes`/`WriteAllBytes` load whole files into memory — fine for
  small files, bad for large and at odds with read-back. The hardened copy should **stream**
  (buffered `Stream` copy + incremental hash), so verify cost is bounded.
- **Performance:** read-back doubles read I/O on copied files only (not skipped ones).
  xxHash keeps CPU negligible, so verification is effectively I/O-bound.
- **Zero-copy vs verification:** kernel zero-copy (`Socket.SendFileAsync`) and verification
  are mutually exclusive (zero-copy means the CPU never sees the bytes), and zero-copy is
  unavailable through TLS/HTTPS SDKs anyway — so it's not pursued; verification wins.
- **ReFS:** our verification overlaps the filesystem's own checksums; acceptable, and we
  can't assume ReFS.
- **Atomicity scope:** the rename is atomic *per file*. A whole-task "all or nothing"
  transaction is out of scope; per-file atomicity already prevents half-written files.
- **Long paths / reparse points:** enumeration follows reparse points today (possible
  loops); a follow-up guard, tracked separately.

---

## 10. Roadmap (lower priority — not needed yet)

Specified to validate the abstraction and record the verification answer for each.

### 10.1 Cloud, S3-compatible
- **Transfer/commit:** no atomic rename — the PUT (or `CompleteMultipartUpload`) *is* the
  atomic commit. Big files need **multipart** (parts ≤5 GB, ≤10,000 parts) with resumable
  retry; abort orphaned multipart uploads via a lifecycle rule.
- **Verification (provider-native — the cheap win):** compute the checksum **while streaming
  the upload** and send it as `Content-MD5` or `x-amz-checksum-*` (**S3 supports CRC32C,
  CRC32, SHA-1, SHA-256**); the server validates on receipt and rejects on mismatch — **no
  read-back / no egress**. `ETag` == MD5 for single-part (not multipart/SSE-KMS).
- **Change detection:** size + ETag/checksum/version (can't set mtime).
- **Wire:** TLS (wire segment only, like SMB3 — §5.3).

### 10.2 SFTP — what verification is possible?
- **Transfer/commit:** SFTP **has atomic rename** and `fsync@openssh.com` (flush to disk),
  so the safe pattern works: upload to a temp name → fsync → rename. Better than S3 here.
- **Wire:** SSH MACs every packet (HMAC-SHA2 / AES-GCM / chacha20-poly1305) — same envelope
  as SMB3 (wire segment only).
- **Content verification — three cases, best first:**
  1. **Server-side hash via the SFTP v6 `check-file` extension** (MD5/SHA-* server-side, no
     download — cheap). Catch: some commercial servers (e.g. Bitvise) implement it, but
     **OpenSSH — the dominant server — does not.** Use only if advertised.
  2. **Remote hash command over an SSH *exec* channel** (`sha256sum`/`xxhsum`) — cheap, but
     needs shell access, which chrooted SFTP-only setups don't grant.
  3. **Download-back + xxHash** — the universal fallback; doubles transfer, and a read right
     after write may be served from the server's page cache.
- **Practical conclusion:** for the common OpenSSH/chroot case, SFTP content verification ≈
  the **mounted-path tier (download-back)** — same expensive read-back, same reason (no
  portable server-side hash). Only specific servers upgrade it to the cheap `check-file`.

### 10.3 Secrets (cloud & SFTP)
Both need credentials. Keep them **out of `tasks.json`**; store via **Windows DPAPI /
Credential Manager** behind an `ISecretStore` seam, referenced by the location only by an
opaque handle. Not needed for phase 1.

---

## 11. Suggested sequencing

1. Introduce `DestinationLocation` (only `LocalDestination`); migrate `Destination.Path` →
   `Destination.Target`. Pure plumbing, no behaviour change.
2. Add streaming + atomic-commit primitives to `IFileSystem` + the fault-injecting fake.
3. Introduce `IDestinationProvider` + registry; `SyncApplier` calls `provider.Write(...)`;
   `SyncPlanner` takes source-fs + dest-provider.
4. Implement `LocalDestinationProvider` = atomic verified copy (§4.A, F) + verified move
   (§4.B) + the network capability profile (§7.6) + content-verify toggle/warning, with
   tests.
5. Mirror guardrail + Recycle Bin (§4.C) + tests.
6. Transient retry (§4.D) + tests.
7. Per-task run lock (§4.E) + tests.
8. Per-destination settings + JSON persistence; wire the content-verify toggle + network
   warning into the editor UI.
9. *(Roadmap)* `ISecretStore`; then S3-compatible provider (§10.1); then SFTP (§10.2).

This keeps phase 1 entirely on local/mounted paths — no secrets, no async backends — while
leaving clean seams for the roadmap providers.
