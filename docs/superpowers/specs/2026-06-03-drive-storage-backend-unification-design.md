# Drive Storage Backend Unification — Design

Date: 2026-06-03
Branch: `s3-inbox-storage`
Supersedes: the "Out of scope: unifying inbox and payload storage abstractions"
decision in `2026-05-27-s3-inbox-storage-design.md`. That spec deliberately chose
a thin per-area abstraction and explicitly deferred unification ("no concrete
second benefit yet"). The S3-inbox bug below is that concrete second benefit.

## Problem

S3 inbox processing fails: every staged item throws `OdinSystemException: File
does not exist <inbox key>` and is marked complete (dropped). Peer transfers
never land when the inbox is on S3.

### Why

The 2026-05-27 work routed the inbox **write** and **cleanup** through
`IInboxReaderWriter` (S3-aware), but the **read** and **payload-promote** paths
still go to disk:

- That spec's process description (step 5) claims `PeerFileWriter.HandleFile`
  reads staged bytes through `InboxStorageManager.GetAllInboxFileBytes`. It does
  not. It reads through `DriveStorageServiceBase.GetAllFileBytesFromTempFileForWriting`,
  which does `fileReaderWriter.GetAllFileBytesAsync(Path.Combine(sourceFolderPath, …))`
  — the **disk** reader, unconditionally. (`PeerFileWriter.cs:51`,
  `PeerFileUpdateWriter.cs:150`, `DriveStorageServiceBase.cs:267`.)
- Payload promotion (`CopyPayloadsAndThumbnailsToLongTermStorage` →
  `LongTermStorageManager.CopyPayloadToLongTermAsync` →
  `IPayloadReaderWriter.CopyPayloadFileAsync`) is implemented for S3 payloads as
  `s3PayloadsStorage.UploadFileAsync(srcLocalPath, dstKey)` — i.e. it reads the
  source from **local disk** and uploads to S3. It works for upload-staging
  (always disk) and for disk-inbox, but not for an S3-staged inbox file, whose
  source is no longer on disk.

So the write went to S3 and the read/promote looked on disk. Confirmed in the
field logs: the only `InboxS3ReaderWriter` calls during a failing run are
`CleanupFileSetAsync`; the error string is verbatim from the disk
`FileReaderWriter.cs:449`; the key is an S3-style relative path handed to
`File.Exists`.

### Root cause (structural)

There is no single concept of "a file in a storage area." Instead:

- **Location** is stringly-typed: `sourceFolderPath` is threaded by hand through
  the whole commit pipeline (~8 call sites set it to `GetDriveUploadPath()` or
  `GetDriveInboxPath()`).
- **Backend** is chosen separately and inconsistently per call: the write uses
  `inboxStorageManager`, the metadata read uses raw `fileReaderWriter`, the
  payload promote uses `payloadReaderWriter`. Three code paths touching the same
  staged file, three opinions about where it lives.
- There are three drifted facades over the same two backends:
  `IInboxReaderWriter` (stream write, `CleanupFileSetAsync`), `IPayloadReaderWriter`
  (byte write, range read, `CopyPayloadFileAsync`, `MoveFileAsync`), and raw
  `FileReaderWriter` (upload).

Adding a backend to one area requires every path that touches that area to
independently know to use the matching backend. The commit path didn't. This is
the "hacks on hacks" engine: each new backend multiplies the places that must be
patched.

## Goals

1. One backend abstraction; **all** staging and long-term blob access routed
   through it.
2. Remove the entire class of bug (write-backend / read-backend mismatch),
   not just the inbox instance.
3. Incremental migration with the **disk path as the behavioral oracle** — never
   a state where the whole system is broken at once.
4. Converge on the end-state: a single S3 on/off switch per tenant.

## Non-goals

- Rewriting working disk logic, the inbox DB queue / `popStamp` coordination, or
  the `TryDirectWriteFile` fast path.
- Mixed backends within a deployment beyond "uploads are always disk" (per the
  all-or-nothing decision, no inbox-S3 + payload-disk combination).
- Migrating existing on-S3 payload data (no forced bucket merge).
- Changing on-disk or on-S3 key layouts (flat `<fileId:N>.<ext>` stays).

## Constraints discovered (these shape the design)

1. **Uploads are always disk; inbox + payload follow the switch.** So the *source*
   of a promote is disk for uploads but disk-or-S3 for inbox. The promote must
   dispatch on the **source** backend, which today it never does (it assumes
   disk).
2. **All-or-nothing** (inbox and payload share the same backend) means the only
   promote source/dest backend combinations that occur are:
   - `(disk → disk)` — S3 off. File copy.
   - `(disk → S3)` — upload-staging into S3 long-term. `UploadFileAsync`
     (disk read + S3 put). *This already works today for uploads.*
   - `(S3 → S3)` — S3 inbox staging into S3 long-term. S3 server-side copy.
     **This is the only new bridge.**
   `(S3 → disk)` never occurs and need not be implemented (assert instead).
3. The S3 primitives already exist on `S3AwsStorage`: `UploadFileAsync`
   (disk→S3), `DownloadFileAsync` (S3→disk), `CopyFileAsync` (S3→S3, same
   bucket), `MoveFileAsync`. The only gap is S3→S3 **across buckets** (if inbox
   and payload buckets differ).

## Decisions locked in brainstorming

- **One unified `IDriveFileStore`, two implementations** (disk, S3). Supersedes
  the 2026-05-27 "do not unify" decision; justified by the bug.
- **Areas are `(backend + root)` bindings**, not independent storage stacks.
  Upload pinned to disk; inbox and long-term payload follow the switch.
- **Promote is a cross-store operation that dispatches on the source backend.**
- **End-state: one S3 switch.** The current `S3Storage` / `S3Payload` / `S3Inbox`
  toggles remain only as migration scaffolding and collapse at the end.
- **Inbox and payload use separate S3 buckets** (decided). This caps the blast
  radius of an inbox delete — the prefix cleanup (`DeleteByPrefixAsync`) and the
  lifecycle-expiration backstop operate on a prefix; a separate bucket makes it
  physically impossible for an over-matching inbox delete to reach a payload
  object. It also avoids migrating existing on-S3 payload data. Consequence: the
  S3→S3 promote is a cross-bucket server-side copy (source = inbox bucket,
  dest = payload bucket).
- `sourceFolderPath` (string) is replaced by a `StagingArea` enum resolved to a
  concrete store in one place.
- The old `IInboxReaderWriter` / `IPayloadReaderWriter` interfaces and their four
  implementations are **deleted outright** in the final step (no adapter shim) —
  internal code with every call site under our control.

## Design

### 1. The unified blob interface

```csharp
public interface IDriveFileStore        // backend-agnostic blob I/O over a path/key
{
    Task<uint>   WriteStreamAsync(string path, Stream stream, CancellationToken ct = default);
    Task         WriteBytesAsync(string path, byte[] bytes, CancellationToken ct = default);
    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken ct = default);
    Task<byte[]> ReadBytesAsync(string path, long start, long length, CancellationToken ct = default); // range (payload serving)
    Task<bool>   ExistsAsync(string path, CancellationToken ct = default);
    Task<long>   LengthAsync(string path, CancellationToken ct = default);
    Task         DeleteAsync(string path, CancellationToken ct = default);
    Task         DeleteSetAsync(string dir, Guid fileId, CancellationToken ct = default); // {fileId:N}.* cleanup
    Task         EnsureDirectoryAsync(string dir, CancellationToken ct = default);          // no-op on S3

    StorageBackendType Backend { get; }  // Disk | S3 — lets the promoter dispatch
}
```

Two implementations, consolidating what already exists:

- **`DiskFileStore`** — the behavior currently in `FileReaderWriter` /
  `InboxFileReaderWriter` / `PayloadFileReaderWriter`.
- **`S3FileStore`** — the behavior currently in `S3AwsStorage` /
  `InboxS3ReaderWriter` / `PayloadS3ReaderWriter`, including the established
  retry policy (5 attempts, exponential backoff, retry 5xx/timeout not 4xx) and
  exception wrapping.

Deliberate tradeoff: one slightly fatter interface (inbox never calls range
reads; payload never calls `DeleteSetAsync`) instead of three drifted ones. The
drift is the bug, so the union wins; ISP purity loses on purpose.

Exception types unify to one `DriveFileStoreException` (subsuming
`InboxReaderWriterException` / `PayloadReaderWriterException`); cancellation
propagates unwrapped, consistent with the current readers.

### 2. Area bindings and backend selection

An area is a store instance bound to `(backend, root prefix)`:

| Area | Backend | Root |
|------|---------|------|
| Upload | always Disk | drive upload path |
| Inbox | switched (Disk \| S3) | drive inbox path |
| Payload long-term | switched (Disk \| S3) | drive payload path |

Backend selection is a single tenant switch in the end-state. During migration
it reads the existing `S3Inbox.Enabled` / `S3Storage.Enabled` so the half-wired
state keeps working; the final step collapses these to one flag and updates the
validation that already ties them together.

DI (`TenantServices`): register the named stores
(`uploadStore` → disk, `inboxStore` → switched, `longTermPayloadStore` →
switched). The existing per-area managers (`InboxStorageManager`,
`UploadStorageManager`, and the blob calls inside `LongTermStorageManager`)
become thin wrappers over their store. `LongTermStorageManager`'s header/DB
responsibilities (transfer history, reactions, file headers) are untouched —
only its payload-blob calls move onto `IDriveFileStore`.

### 3. The promote operation

"Promote" is the step that moves a file out of temporary staging into permanent
long-term storage. For a peer transfer: incoming bytes are written to inbox
staging, and at commit the inbox processor copies them into long-term storage
(where served files live) and deletes the staging copy.

Both ends sit on a backend — staging is disk (upload) or disk-or-S3 (inbox),
long-term is disk-or-S3. Today the promote is a single hardcoded operation: open
a file **on local disk** and write it to long-term (`CopyPayloadFileAsync` →
`UploadFileAsync(localDiskPath, dstKey)`). It varies the *destination* backend
but always reads the source from disk. That assumption breaks the instant inbox
staging is on S3 — the source is no longer on disk — which is the payload
analogue of the metadata-read bug.

The fix is a single cross-store operation that dispatches on **both** ends'
backends, replacing the disk-assuming `CopyPayloadFileAsync` usage:

```csharp
// Move/copy a staged file into long-term, bridging backends as needed.
Task PromoteAsync(IDriveFileStore source, string sourcePath,
                  IDriveFileStore dest,   string destPath, CancellationToken ct);
```

Dispatch on `(source.Backend, dest.Backend)`:

| source → dest | implementation |
|---------------|----------------|
| Disk → Disk | local file copy |
| Disk → S3 | `S3.UploadFileAsync(localSrc, dstKey)` (today's payload path) |
| S3 → S3 | S3 server-side copy (`CopyObject`); cross-bucket when inbox and payload buckets differ |
| S3 → Disk | does not occur under all-or-nothing — assert/throw |

Metadata and other small staged files are read with `source.ReadAllBytesAsync`
(no local-disk assumption). The S3→S3 cross-bucket copy is the one new primitive:
extend `S3AwsStorage` with a copy that takes an explicit source bucket
(`CopyObjectRequest` already supports it), used only when the two stores'
buckets differ.

### 4. Replacing `sourceFolderPath`

Today the commit code is told where a staged file lives via a **string**,
`sourceFolderPath` (e.g. `".../inbox/drives/<driveId>"`). It does
`Path.Combine(sourceFolderPath, "<fileId>.<ext>")` and hands the result to the
**disk** reader. The string says *where* but not *on which backend* — it silently
assumes disk. When inbox staging moved to S3, that same string became an S3 key
the code still read off disk → "File does not exist."

Replace the string with a typed label that carries the backend. Introduce
`enum StagingArea { Upload, Inbox }` and thread it through the commit pipeline
(`HandleFile` → `WriteNewFile`/`OverwriteFile` → `CommitNewFile`/`OverwriteFile`/
`UpdateBatchAsync` → `CopyPayloadsAndThumbnailsToLongTermStorage` and
`GetAllFileBytesFromTempFileForWriting`) in place of `sourceFolderPath`. The ~8
call sites already compute `IsDirectWrite ? Upload : Inbox`; they pass the enum
instead of a path.

Resolution happens in exactly one place — `DriveStorageServiceBase` maps
`StagingArea` to the concrete store that knows both its location and its backend
(`Upload → uploadStore`, always disk; `Inbox → inboxStore`, disk or S3 per
config). Everything below the resolution point calls `store.ReadAllBytesAsync(…)`
and never builds a path or picks a backend, so the write-backend / read-backend
mismatch becomes impossible. (Passing the store object directly is an equivalent
variant; the essential change is replacing a string that implies disk with a
handle that carries the backend.)

This also retires the temporary path-string dispatch added to
`GetAllFileBytesFromTempFileForWriting` (the interim S3-inbox metadata fix).

### 5. The corrected pipeline (after refactor)

- **Stage (incoming transfer):** `inboxStore.WriteStreamAsync` (unchanged from
  the S3-inbox work).
- **Read metadata (processing):** `resolve(StagingArea).ReadAllBytesAsync` —
  hits the same backend the write used.
- **Promote payloads/thumbs (commit):** `PromoteAsync(sourceStore, …,
  longTermPayloadStore, …)` — bridges backends per the table above.
- **Cleanup:** `inboxStore.DeleteSetAsync` (unchanged).

## Migration sequencing (incremental; disk stays green throughout)

1. **Characterization tests first.** Lock current disk behavior of the commit /
   promote path (peer inbox + direct write + local upload, with and without
   payloads) before touching it. The inbox reader/manager tests already added
   this session are the starting point.
2. **Introduce `IDriveFileStore` + `DiskFileStore`**, implemented by delegating
   to today's `FileReaderWriter`. Wire `uploadStore`/`inboxStore`/
   `longTermPayloadStore` as disk instances. No behavior change; tests stay green.
3. **Introduce `S3FileStore`** by delegating to today's `S3AwsStorage`; switch
   inbox/payload stores to it under the existing flags. Inbox + payload readers
   become thin adapters or are retired.
4. **Introduce `PromoteAsync`** and route `CopyPayloadsAndThumbnailsToLongTermStorage`
   through it. Add the S3→S3 (incl. cross-bucket) bridge. This is the step that
   actually fixes S3-inbox payloads.
5. **Replace `sourceFolderPath` with `StagingArea`** end to end; delete the
   interim path-compare dispatch.
6. **Collapse the config** to a single S3 switch and **delete**
   `IInboxReaderWriter`/`IPayloadReaderWriter` and their four implementations
   outright once nothing references them (no adapter shim).

Each step is independently shippable and reversible; only step 4 changes S3
behavior, and disk behavior is invariant across all of them.

## Testing

- **Disk characterization (oracle):** peer inbox transfer and local upload,
  with payloads + thumbnails, asserting bytes land in long-term and staging is
  cleaned. Must pass unchanged at every step.
- **Store unit tests:** `DiskFileStore` and `S3FileStore` against the existing
  `RUN_S3_TESTS` / MinIO harness (Docker-gated; runs in CI, not the sandbox),
  plus mock-based tests for retry/exception/cancellation as already done for the
  inbox reader.
- **`PromoteAsync` matrix:** disk→disk, disk→S3, S3→S3 (same bucket and
  cross-bucket); S3→disk asserts. The S3 cells under `RUN_S3_TESTS`.
- **End-to-end S3 inbox:** peer transfer with payloads, inbox on S3, processed
  and committed — the scenario that currently fails. Manual/MinIO until a
  harness exists.
- **Regression:** existing `_Universal/` peer-transfer suite passes with S3 off
  (default) at every migration step.

## Error handling

- Transient S3 failures (5xx/timeout) retried via the shared policy; 4xx not
  retried; non-cancellation failures wrapped in `DriveFileStoreException`;
  cancellation propagates unwrapped.
- `CleanupInboxFiles` keeps its swallow-and-log behavior (best-effort cleanup);
  the S3 lifecycle expiration backstop from the 2026-05-27 spec is retained.
- A missing staged file still surfaces to the processor's catch arms as today
  (item treated as `DeleteFromInbox`), now from the correct backend.

## Risks and rollback

- **Risk:** the refactor touches the commit path that writes user files.
  **Mitigation:** disk-path characterization tests pass at every step; steps are
  small and individually reversible; only step 4 alters S3 behavior.
- **Risk:** S3→S3 cross-bucket copy semantics (permissions, region).
  **Mitigation:** covered by the promote matrix tests; falls back cleanly when
  inbox and payload share a bucket.
- **Rollback:** S3 off restores pure-disk behavior at any step; the feature is
  per-tenant opt-in.
