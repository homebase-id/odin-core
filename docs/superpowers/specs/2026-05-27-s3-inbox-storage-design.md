# S3 Inbox Storage — Design

Date: 2026-05-27
Branch: `s3-inbox-storage`

## Goal

Make peer-transfer **inbox staging** optionally back onto S3 (object storage)
instead of local disk, selected by configuration, mirroring the existing
payload storage pattern. Primary drivers:

1. **Multi-node / ephemeral containers.** Inbox staging on local disk assumes
   the node that *received* a transfer is the node that *processes* it (the
   inbox DB row points at files only that node can see). S3 staging makes
   processing node-independent: any node that pops the inbox row can read the
   staged bytes. This is the core correctness fix for horizontal scaling.
2. **Capacity / cost offload.** Move staging bytes off the host onto cheaper
   object storage.

Disk staging remains the default; S3 is opt-in per tenant via config.

**Multi-tenancy:** the inbox bucket is **shared across tenants**, isolated by a
tenant-id key prefix (`inbox/<tenant-id>/drives/…`), mirroring payloads. No
bucket-per-tenant.

## Background: how inbox processing works today

Incoming peer transfers are a two-tier queue — a DB row carries the
queue/metadata; the file bytes are staged separately.

**Receive → stage → enqueue**
1. `PeerIncomingDriveUploadController.ReceiveIncomingTransfer` parses the
   multipart POST (transferkeyheader, metadata, payloads, thumbnails).
2. `PeerDriveIncomingTransferService` mints an `InternalDriveFileId` and writes
   each part via `fileSystem.Storage.WriteInboxStream(...)` →
   `InboxStorageManager.WriteInboxStream` → `FileReaderWriter.WriteStreamAsync`.
   Files land at `…/inbox/drives/<driveId>/<fileId:N>.<ext>` (`.metadata`,
   `.transferkeyheader`, `.payload`, `.thumb`).
3. `FinalizeTransfer` tries `TryDirectWriteFile` (skip the queue); on failure it
   calls `RouteToInboxAsync`, inserting a row into the `inbox` table
   (`TransitInboxBoxStorage.AddAsync`). The row's `value` blob is the serialized
   `TransferInboxItem` (sender, instruction type, encrypted key header) — **not**
   the file bytes.

**Process → store → cleanup**
4. `PeerInboxProcessorBackgroundService` polls; `PeerInboxProcessor.ProcessInboxAsync`
   pops rows (atomic `popStamp` reserve), batch of 100.
5. `PeerFileWriter.HandleFile` reads staged metadata/payload bytes back through
   `fileSystem.Storage` (→ `InboxStorageManager.GetAllInboxFileBytes`) and calls
   `StoreNormalFileLongTermAsync` to move payloads into long-term storage
   (disk or S3 via `IPayloadReaderWriter`).
6. Success → delete row + `CleanupInboxFiles` (globs `<fileId>.*` off disk).
   Failure → reset `popStamp`.

**Key asymmetry being closed:** long-term payloads already have a clean
abstraction — `IPayloadReaderWriter` with `PayloadFileReaderWriter` (disk) and
`PayloadS3ReaderWriter` (S3), DI-selected by `S3Storage.Enabled`. Inbox staging
has no such abstraction: `InboxStorageManager` calls `FileReaderWriter` directly
and uses a `Directory.GetFiles` glob for cleanup. `S3AwsInboxStorage` /
`IS3InboxStorage` and `S3Inbox:*` config already exist but nothing is wired to
them.

## Approach (chosen)

**Thin storage abstraction, mirroring the payload pattern.** Smallest change
that is still correct and consistent with a pattern the team already committed
to. Rejected alternatives: unifying inbox + payload under one shared abstraction
(touches working payload code, no concrete second benefit yet); rethinking the
staging→process flow (largest change, highest risk, out of scope for this goal).

### Decisions locked during brainstorming

- **Stream-based write.** Inbox payloads can be large; keep `WriteInboxStream`
  stream-based to avoid buffering whole payloads in memory. (Diverges slightly
  from `IPayloadReaderWriter`'s `byte[]` shape — accepted.)
- **Identical directory layout on disk and S3.** Flat `<fileId:N>.<ext>` keys on
  both backends. Cleanup therefore needs delete-by-prefix on S3 (not a
  per-fileId folder). Add `DeleteByPrefixAsync` to the S3 layer rather than
  reshaping keys.

## Components

### 1. `IInboxReaderWriter` (new)

`src/services/Odin.Services/Drives/DriveCore/Storage/IInboxReaderWriter.cs`,
mirroring `IPayloadReaderWriter`.

```csharp
public interface IInboxReaderWriter
{
    Task<uint>   WriteStreamAsync(string path, Stream stream, CancellationToken ct = default);
    Task<byte[]> GetAllFileBytesAsync(string path, CancellationToken ct = default);
    Task<bool>   FileExistsAsync(string path, CancellationToken ct = default);
    Task CreateDirectoryAsync(string dir, CancellationToken ct = default);          // no-op on S3
    Task CleanupFileSetAsync(string driveDir, Guid fileId, CancellationToken ct = default);
}
```

Plus an `InboxReaderWriterException` analogous to `PayloadReaderWriterException`.

- **`InboxFileReaderWriter`** (disk) — wraps the existing `FileReaderWriter`.
  - `WriteStreamAsync` → `FileReaderWriter.WriteStreamAsync` (after
    `CreateDirectory`).
  - `GetAllFileBytesAsync` → `FileReaderWriter.GetAllFileBytesAsync`.
  - `FileExistsAsync` → `FileReaderWriter.FileExists`.
  - `CreateDirectoryAsync` → `FileReaderWriter.CreateDirectory`.
  - `CleanupFileSetAsync` → today's glob: `Directory.GetFiles(driveDir, "<fileId:N>.*")`
    then `FileReaderWriter.DeleteFiles(...)`. Carries over the existing
    "glob is authoritative for orphan cleanup" rationale (see the long comment
    in `InboxStorageManager.CleanupInboxFiles`).

- **`InboxS3ReaderWriter`** (S3) — wraps `IS3InboxStorage`.
  - `WriteStreamAsync` → `IS3Storage.WriteStreamAsync`.
  - `GetAllFileBytesAsync` → `IS3Storage.ReadBytesAsync`.
  - `FileExistsAsync` → `IS3Storage.FileExistsAsync`.
  - `CreateDirectoryAsync` → no-op (S3 has no directories).
  - `CleanupFileSetAsync` → `DeleteByPrefixAsync($"{driveDir}/{fileId:N}.")`.
  - Reuses the same retry policy as `PayloadS3ReaderWriter`: `TryRetry` 5
    attempts, exponential backoff 5s, retry on 5xx/timeout, not on 4xx; wrap
    non-cancellation exceptions in `InboxReaderWriterException`.

### 2. `IS3Storage.DeleteByPrefixAsync` (new method)

`src/core/Odin.Core.Storage/ObjectStorage/IS3Storage.cs` +
`S3AwsStorage.cs`.

**This goes on the base `S3AwsStorage` class (declared on `IS3Storage`), not on
`S3AwsInboxStorage`.** The inbox subclass adds no members — it inherits
`DeleteByPrefixAsync` like every other S3 storage. Any future S3 consumer
(payloads included) gets it for free. `InboxS3ReaderWriter` simply calls the
inherited method via its `IS3InboxStorage` reference.

`DeleteDirectoryAsync` already runs a paginated `ListObjectsV2` + `DeleteObjects`
loop; it differs from what we need only by `S3Path.AssertFolderName(path)`.
Extract the loop into a private helper `DeletePrefixInternalAsync(string prefix, ct)`
on `S3AwsStorage`:

- `DeleteDirectoryAsync` → keeps `AssertFolderName`, then calls the helper.
- `DeleteByPrefixAsync(string prefix, ct)` → calls the helper directly with a raw
  prefix (no folder assertion). The helper still does `S3Path.Combine(_rootPath, prefix)`
  before listing, exactly like `DeleteDirectoryAsync`, so the `"inbox"` rootPath
  is applied. Prefix passed by the inbox writer is
  `<tenant-id>/drives/<driveId>/<fileId:N>.`, which matches `<fileId>.payload`,
  `.metadata`, etc. — exactly the disk glob's semantics.

### 3. `InboxStorageManager` (modified)

`src/services/Odin.Services/Drives/DriveCore/Storage/InboxStorageManager.cs`.

Depend on `IInboxReaderWriter` instead of `FileReaderWriter`. Delegate:
- `WriteInboxStream` → `CreateDirectoryAsync` + `WriteStreamAsync`.
- `GetAllInboxFileBytes` → `GetAllFileBytesAsync`.
- `InboxFileExists` → `FileExistsAsync`.
- `CleanupInboxFiles` → `CleanupFileSetAsync(driveDir, file.FileId)`; remove the
  `Directory`/glob logic from the manager (it moves into the disk impl).

The manager no longer references `System.IO.Directory` directly.

### 4. `TenantPathManager` (modified)

`src/services/Odin.Services/Drives/FileSystem/Base/TenantPathManager.cs`.

Add `S3InboxEnabled` from `config.S3Inbox.Enabled`, mirroring `S3PayloadsEnabled`
**precisely** — including how the rootPath folder is supplied.

How payloads do it (the model to copy): when `S3PayloadsEnabled`,
`RootPayloadsPath = string.Empty`, so `PayloadsDrivesPath` becomes
`<tenant-id>/drives`. The `"payloads"` folder is **not** in the
`TenantPathManager` path — it is prepended by the S3 layer's `rootPath`
(`S3AwsPayloadStorage` constructed with `rootPath = "payloads"`). Final key:
`payloads/<tenant-id>/drives/...`. Putting `"payloads"` in both places would
double it; hence the empty root.

Apply the same to inbox:
- When **disabled** (default): current on-disk paths unchanged —
  `InboxDrivesPath` = `…/registrations/<tenant-id>/inbox/drives`, file path
  `…/<driveId>/<fileId:N>.<ext>`.
- When **enabled**: anchor the inbox path to the bucket root with the tenant
  first and **without** the `inbox` folder and **without** `registrations` —
  `InboxDrivesPath` = `<tenant-id>/drives` — producing the relative key
  `<tenant-id>/drives/<driveId>/<fileId:N>.<ext>`. The `"inbox"` folder is
  supplied by `S3AwsInboxStorage`'s `rootPath` (`S3Inbox.RootPath`, default
  `"inbox"`), giving the final key `inbox/<tenant-id>/drives/<driveId>/<fileId:N>.<ext>`.
  This matches the payload structure exactly. The path string is the only thing
  that differs by backend; the inbox reader/writer consumes it as either a disk
  path or an S3 key.

Note: `InboxPath`/`InboxDrivesPath` are currently derived from
`RegistrationPath`. The S3 branch must bypass that derivation (tenant-first,
bucket-root-anchored), not just swap a root constant, since the on-disk layout
nests inbox under `registrations/<tenant>/` while the S3 layout is
`<tenant>/drives` like payloads.

### 5. DI wiring

`src/apps/Odin.Hosting/TenantServices.cs`, beside the payload block (~366-374):

```csharp
if (odinConfig.S3Inbox.Enabled)
    cb.RegisterType<InboxS3ReaderWriter>().As<IInboxReaderWriter>().SingleInstance();
else
    cb.RegisterType<InboxFileReaderWriter>().As<IInboxReaderWriter>().SingleInstance();
```

`S3Inbox.Enabled` already validates `S3Storage.Enabled` and registers
`IS3InboxStorage` in `SystemServices` (existing `AddS3AwsInboxStorage`).
`InboxStorageManager` stays `InstancePerLifetimeScope`; the reader/writer is
`SingleInstance` like the payload one.

### 6. Orphan / partial-staging backstop (configurable, default off)

Two crash windows leave staged bytes with no completing row:
crash-between-upload-and-row-insert, and crash-between-pop-and-cleanup. The
backstop is an **S3 lifecycle expiration rule** on the inbox prefix, rather than
a custom sweeper.

**It must be configurable and default to no expiration.** Add
`S3Inbox:ExpirationDays` (int, default `0`):

- `0` (default) → **no expiration**. The host applies/ensures no lifecycle
  expiration rule; staged objects live until normal processing cleans them up.
  This is the safe default — nothing is auto-deleted.
- `> 0` → at startup the host ensures an S3 lifecycle rule on the inbox bucket
  scoped to the `S3Inbox.RootPath` prefix that expires objects older than N
  days, via `PutBucketLifecycleConfigurationAsync`. Reconciled idempotently so
  changing the value updates the rule and setting it back to `0` removes it.

Only meaningful when `S3Inbox.Enabled` is true. The day granularity matches what
S3 lifecycle natively supports (expiration is day-based, not hours). This also
serves the cost-offload goal when operators opt in.

The existing disk-only `InboxOrphanScanBackgroundService` walks the on-disk
staging dirs; under S3 inbox `InboxDrivesPath` is an object-key prefix with
nothing on disk, so that scanner skips itself when `S3InboxEnabled` and the
lifecycle rule above is the sole backstop. Because `ExpirationDays` defaults to
`0`, the default S3-inbox config has no automatic orphan cleanup — operators who
want one set `ExpirationDays > 0`.

## Out of scope

- `TryDirectWriteFile` fast path — stays node-local, unaffected.
- `PeerFileWriter` / `PeerInboxProcessor` logic — unchanged.
- The inbox DB queue — already coordinates cross-node via `popStamp`.
- Unifying inbox and payload storage abstractions.

## Error handling

- S3 transient failures (5xx, timeouts) retried via the shared `TryRetry`
  policy; 4xx not retried. Non-cancellation exceptions surface as
  `InboxReaderWriterException`.
- Cleanup failures are already swallowed-and-logged in `CleanupInboxFiles`;
  preserve that behavior. The S3 lifecycle rule is the durable backstop.
- Reads of a missing staged file behave as today (the processor's catch arms
  treat a failed item as `DeleteFromInbox`).

## Testing

- `InboxFileReaderWriter` and `InboxS3ReaderWriter` unit/integration tests
  against the existing S3 test harness used by the payload S3 tests
  (`RUN_S3_TESTS` / MinIO). Note: Docker is unavailable in the sandbox, so the
  S3 path runs in CI, not locally.
- `S3AwsStorage.DeleteByPrefixAsync` test: stage several keys sharing a
  `<driveId>/<fileId>.` prefix plus a sibling fileId; assert only the matching
  set is deleted.
- `InboxStorageManager` test: write the full file set (metadata,
  transferkeyheader, payload, thumb), then `CleanupInboxFiles`; assert the set
  is gone, on both backends.
- `TenantPathManager` test: `S3InboxEnabled` true/false produces the expected
  anchored vs on-disk paths with identical flat filenames.
- Lifecycle reconciliation test: `ExpirationDays = 0` ensures no expiration rule
  exists on the inbox prefix; `ExpirationDays > 0` ensures a rule with the
  expected day count; changing the value updates it; back to `0` removes it.
  (S3 harness, `RUN_S3_TESTS`.)
- Existing peer-transfer integration tests (`_Universal/`) continue to pass with
  S3 inbox disabled (default), confirming no regression.

## Config

```jsonc
"S3Storage": { "Enabled": false, "AccessKey": "...", "SecretAccessKey": "...",
               "ServiceUrl": "...", "Region": "...", "ForcePathStyle": false },
"S3Inbox":   { "Enabled": false, "BucketName": "your-s3-inbox-bucket-name-here",
               "ExpirationDays": 0 }
// S3Inbox:RootPath defaults to "inbox"; S3Inbox requires S3Storage:Enabled.
// S3Inbox:ExpirationDays defaults to 0 (no expiration).
```

`S3Storage`, `S3Inbox:Enabled`, `BucketName`, `RootPath` already exist.
**New:** `S3Inbox:ExpirationDays` (add `ExpirationDays` to `S3InboxSection`,
default `0`).
