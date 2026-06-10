# Drive Storage Backend Unification — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the three drifted storage facades (`IInboxReaderWriter`, `IPayloadReaderWriter`, raw `FileReaderWriter`) with one backend-aware `IDriveFileStore`, route all staging and long-term blob access through it, and make staged-file reads and payload promotion work when the inbox is on S3.

**Architecture:** One blob interface (`IDriveFileStore`) with `DiskFileStore` and `S3FileStore` implementations. Each storage *area* (upload, inbox, long-term payload) is that interface bound to a backend + root. Uploads are always disk; inbox and long-term payload follow a single S3 switch. Promotion (staging → long-term) is a cross-store operation that dispatches on the source backend. The stringly-typed `sourceFolderPath` is replaced by a `StagingArea` enum resolved to a store in one place.

**Tech Stack:** .NET 9, C#, Autofac DI, NUnit, Moq, AWSSDK.S3, MinIO (S3 integration tests, gated by `RUN_S3_TESTS`).

---

## CONTEXT FOR A FRESH AGENT (read this first)

You have **no memory** of the conversation that produced this plan. Everything you need is here and in the linked spec. Read this whole section before starting Task 1.

### The bug this fixes

ODIN-CORE stages incoming peer-transfer files (and local uploads) in a temporary
area, then commits them into long-term storage. A 2026-05-27 feature made the
**inbox** staging area optionally back onto **S3** instead of local disk
(`S3Inbox.Enabled`). It wired the inbox **write** and **cleanup** through an
S3-aware abstraction, but the **read** and **payload-promote** paths still go to
local disk. So with the inbox on S3, every staged item fails to read back:

```
OdinSystemException: File does not exist 20000000-…/drives/…/<fileId>.metadata
```

and the inbox processor marks the item complete (dropped). Peer transfers never
land when the inbox is on S3.

### Root cause (structural)

There is no single concept of "a file in a storage area." Instead:

- **Location** is a string, `sourceFolderPath`, threaded by hand through the
  commit pipeline.
- **Backend** is chosen separately and inconsistently per call: the write uses
  `inboxStorageManager`, the metadata read uses raw `fileReaderWriter` (disk),
  the payload promote uses `payloadReaderWriter`.
- Three drifted interfaces over the same two backends:
  `IInboxReaderWriter` (stream write + `CleanupFileSetAsync`),
  `IPayloadReaderWriter` (byte write + range read + `CopyPayloadFileAsync`),
  raw `FileReaderWriter` (upload, disk only).

The two specific disk-bound reads:

1. `DriveStorageServiceBase.GetAllFileBytesFromTempFileForWriting`
   (`src/services/Odin.Services/Drives/FileSystem/Base/DriveStorageServiceBase.cs:267`)
   reads staged metadata via the **disk** `fileReaderWriter`. Called by
   `PeerFileWriter.HandleFile` (`…/Peer/Incoming/Drive/Transfer/PeerFileWriter.cs:51`)
   and `PeerFileUpdateWriter` (`…/FileUpdate/PeerFileUpdateWriter.cs:150`).
2. Payload promotion: `CopyPayloadsAndThumbnailsToLongTermStorage`
   (`DriveStorageServiceBase.cs:1719`) → `LongTermStorageManager.CopyPayloadToLongTermAsync`
   (`…/DriveCore/Storage/LongTermStorageManager.cs:374`) →
   `IPayloadReaderWriter.CopyPayloadFileAsync`. For S3 payloads that is
   `s3PayloadsStorage.UploadFileAsync(localSrcPath, dstKey)` — it reads the
   source **from local disk**. Works for disk-staged sources, fails for an
   S3-staged inbox source. (Latent: the field logs that produced this plan used
   0-payload chat messages, so only failure #1 fired. #2 fires for any message
   with payloads.)

### The design (full detail in the spec — read it)

**Spec (committed):** `docs/superpowers/specs/2026-06-03-drive-storage-backend-unification-design.md`
(commit `d3e881d4c`). It supersedes the "do not unify inbox + payload" decision
in `docs/superpowers/specs/2026-05-27-s3-inbox-storage-design.md`.

Locked decisions:
- **All-or-nothing backends:** a tenant is either fully disk or fully S3.
  Uploads are **always disk**; inbox and long-term payload follow one switch.
  So the only promote source→dest backend combos are `disk→disk`, `disk→S3`
  (uploads, already works today), and `S3→S3` (the new bridge). `S3→disk` cannot
  occur and is an assert.
- **Inbox and payload use separate S3 buckets** (caps the blast radius of inbox
  prefix-deletes / lifecycle expiration so they can never touch a payload
  object; also avoids migrating existing on-S3 payload data). The `S3→S3`
  promote is therefore a **cross-bucket** server-side copy.
- `sourceFolderPath` (string) → `StagingArea { Upload, Inbox }` enum, resolved to
  a concrete store in exactly one place (`DriveStorageServiceBase`).
- The old `IInboxReaderWriter`/`IPayloadReaderWriter` and their four
  implementations are **deleted outright** at the end (no adapter shim).

### Current repo state (already on branch `s3-inbox-storage`)

Uncommitted working-tree changes from the diagnosis session — **do not assume a
clean tree**:

- `DriveStorageServiceBase.GetAllFileBytesFromTempFileForWriting` already has an
  **interim path-compare fix**: if `sourceFolderPath` equals the drive's inbox
  path it reads via `inboxStorageManager.GetAllInboxFileBytes`, else disk. This
  unblocks metadata reads (0-payload messages) but is the stringly-typed hack
  Phase 5 removes. Keep it working until Phase 5.
- `InboxS3ReaderWriter` retry attempts/backoff are config-driven
  (`OdinConfiguration.S3InboxSection.RetryAttempts` / `RetryInitialBackoffMs`,
  defaults 5 / 5000).
- Tests already added this session (keep, they're your starting safety net):
  `tests/services/Odin.Services.Tests/Drives/DriveCore/Storage/InboxS3ReaderWriterTests.cs`
  (one ungated `InboxS3ReaderWriterUnitTests` mock class + one
  `#if RUN_S3_TESTS` MinIO class), and expanded `InboxFileReaderWriterTests` /
  `InboxStorageManagerTests`.

### Key files and current surfaces (so you don't have to rediscover them)

Storage primitives:
- `src/services/Odin.Services/Drives/DriveCore/Storage/FileReaderWriter.cs` (disk):
  `WriteStreamAsync(path,stream)→Task<uint>`, `WriteAllBytesAsync(path,bytes,ct)`,
  `GetAllFileBytesAsync(path)→Task<byte[]>`,
  `GetFileBytesAsync(path,offset,length,ct)→Task<byte[]>`, `MoveFile`,
  `CopyPayloadFile(src,dst)`, `OpenStreamForReading(path)→Stream`, `DeleteFile`,
  `DeleteFiles(IEnumerable)`, `FileExists→bool`, `DirectoryExists→bool`,
  `CreateDirectory(dir)`. Retry is internal (driven by
  `OdinConfiguration.Host.FileOperationRetryAttempts/DelayMs`).
- `src/core/Odin.Core.Storage/ObjectStorage/IS3Storage.cs` + `S3AwsStorage.cs`
  (S3): `BucketName`, `WriteStreamAsync(path,stream,ct)→Task<long>`,
  `WriteBytesAsync(path,bytes,ct)`, `ReadBytesAsync(path,ct)→Task<byte[]>`,
  `ReadBytesAsync(path,offset,length,ct)→Task<byte[]>`,
  `FileExistsAsync(path,ct)→Task<bool>`, `FileLengthAsync(path,ct)→Task<long>`,
  `DeleteFileAsync`, `DeleteByPrefixAsync(prefix,ct)`,
  `CopyFileAsync(src,dst,ct)` (same bucket), `MoveFileAsync`,
  `UploadFileAsync(localSrc,dstKey,ct)` (disk→S3),
  `DownloadFileAsync(srcKey,localDst,ct)` (S3→disk). All wrap failures as
  `S3StorageException(message, innerAmazonS3Exception)`.
- Readers being unified: `InboxFileReaderWriter`, `InboxS3ReaderWriter`
  (`IInboxReaderWriter`); `PayloadFileReaderWriter`, `PayloadS3ReaderWriter`
  (`IPayloadReaderWriter`). All in
  `src/services/Odin.Services/Drives/DriveCore/Storage/`.

Area managers (`src/services/Odin.Services/Drives/DriveCore/Storage/`):
- `InboxStorageManager(IInboxReaderWriter, ILogger, TenantContext)` —
  `WriteInboxStream`, `GetAllInboxFileBytes`, `InboxFileExists`,
  `CleanupInboxFiles`.
- `UploadStorageManager(FileReaderWriter, …)` — `WriteUploadStream`,
  `GetAllUploadFileBytes`, `UploadFileExists` (disk only).
- `LongTermStorageManager(IPayloadReaderWriter, …)` — payload blob ops
  (`CopyPayloadToLongTermAsync`, `CopyThumbnailToLongTermAsync`,
  `GetPayloadStreamAsync`, `PayloadExistsOnDiskAsync`, `PayloadLengthAsync`,
  `GetThumbnailStreamAsync`, `TryHardDeleteListOfPayloadFiles`, …) **plus**
  header/DB ops (transfer history, reactions, file headers) that are NOT storage
  and must NOT move.

Orchestrator:
- `src/services/Odin.Services/Drives/FileSystem/Base/DriveStorageServiceBase.cs`
  primary ctor injects `LongTermStorageManager longTermStorageManager`,
  `UploadStorageManager uploadStorageManager`, `InboxStorageManager
  inboxStorageManager`, `FileReaderWriter fileReaderWriter`, `IDriveManager
  driveManager`, `IdentityDatabase db`. `DriveManager` is exposed as a protected
  property.

Path building (`…/Drives/FileSystem/Base/TenantPathManager.cs`):
- `GetDriveInboxPath(driveId)`, `GetDriveUploadPath(driveId)`,
  `GetDrivePayloadPath(driveId)`, `static GetFilename(fileId, extension)`,
  `static GetBasePayloadFileNameAndExtension(key, uid)`,
  `static GetThumbnailFileNameAndExtension(key, uid, w, h)`.

DI:
- `src/apps/Odin.Hosting/TenantServices.cs` ~373-384: payload reader/writer
  selected by `odinConfig.S3Storage.Enabled`; inbox reader/writer by
  `odinConfig.S3Inbox.Enabled`.
- `src/apps/Odin.Hosting/SystemServices.cs` ~266-293: registers the S3 client
  (`S3Storage.Enabled`), `AddS3AwsPayloadStorage(S3Payload.BucketName,
  S3Payload.RootPath)` (`S3Payload.Enabled`), `AddS3AwsInboxStorage(
  S3Inbox.BucketName, S3Inbox.RootPath)` (`S3Inbox.Enabled`).
- Config sections in `…/Configuration/OdinConfiguration.cs`: `S3StorageSection`
  (connection), `S3PayloadSection` (BucketName, RootPath="payloads"),
  `S3InboxSection` (BucketName, RootPath="inbox", RetryAttempts,
  RetryInitialBackoffMs).

The ~8 `sourceFolderPath` call sites Phase 5 changes (all set it to
`drive.GetDriveUploadPath()` or `drive.GetDriveInboxPath()`):
`PeerInboxProcessor.cs:343,365,387,457`;
`PeerDriveIncomingTransferService.cs:381,397`;
`PeerDriveIncomingFileUpdateService.cs:193` (assigns, used at 199,214);
`StandardFileStreamWriter.cs:64,94`; `CommentStreamWriter.cs:75,109`;
`FileSystemUpdateWriterBase.cs:286,342`. The method that consumes it inside the
pipeline: `GetAllFileBytesFromTempFileForWriting`, `CommitNewFile`,
`OverwriteFile`, `UpdateBatchAsync`, `CopyPayloadsAndThumbnailsToLongTermStorage`
(all in `DriveStorageServiceBase.cs`).

### Environment gotchas

- **`RUN_S3_TESTS` only compiles for `USER=seb`** (see the test `.csproj`
  `DefineConstants` condition). MinIO/Docker is **absent in the sandbox**, so
  every `#if RUN_S3_TESTS` test runs in CI, not locally. To compile-check gated
  code locally: `USER=seb dotnet build ./tests/services/Odin.Services.Tests/Odin.Services.Tests.csproj`.
- Mock-based unit tests (Moq over the interfaces) need **no** Docker/S3 and must
  live **outside** any `#if RUN_S3_TESTS` guard so they always run.
- **Scoped-connection parallelism:** `ScopedConnectionFactory` is one
  connection/transaction per IOC scope and throws
  `"Parallelism detected"` if two tasks touch the DB through the same scope. Do
  not fan out DB work across the request scope. (Not central to this plan, but
  relevant if you add background work.)
- Build: `dotnet build ./odin-core.sln`. Targeted test:
  `dotnet test ./tests/services/Odin.Services.Tests/Odin.Services.Tests.csproj --filter "FullyQualifiedName~<Name>"`.
- Commit message policy (repo owner): **no** `Co-Authored-By`/`Generated with`
  trailers. Do not put a slash in any git branch name.

### Migration invariant

**Disk behavior is the oracle and must never break.** Phases 1-3, 5, 6 are
behavior-preserving for disk. Only Phase 4 changes S3 behavior. Run the Phase 1
characterization tests after every task.

---

## Phase 1 — Characterization tests (the safety net)

Lock current **disk** behavior of the commit/promote path before touching it.
These are integration-style tests using the real `WebScaffold` peer-transfer
infrastructure so they exercise the whole pipeline (stage → read → promote →
cleanup), which is exactly where the bug lives.

### Task 1: Peer-transfer-with-payload characterization test (disk)

**Files:**
- Test: `tests/apps/Odin.Hosting.Tests/_Universal/DriveStorage/InboxPromotionCharacterizationTests.cs` (create)

Use the peer file-transfer pattern from `CLAUDE.md` (two identities, circle with
`DrivePermission.Write`, connect, upload with `TransitOptions { Recipients,
AllowDistribution = true }`, `WaitForEmptyOutbox()`, `ProcessInbox()`, then
`QueryByGlobalTransitId`). Inbox is on disk (default config — do **not** enable
S3). The test asserts the *committed* state, which is what must stay invariant.

- [ ] **Step 1: Write the failing test.**

```csharp
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal; // OwnerApiClientRedux, TestIdentities, etc.

namespace Odin.Hosting.Tests._Universal.DriveStorage;

public class InboxPromotionCharacterizationTests
{
    // Mirror an existing _Universal peer-transfer test class for scaffold setup
    // (RunBeforeAnyTests with Frodo + Samwise). See e.g. the read-receipt or
    // file-transfer tests under _Universal for the exact WebScaffold boilerplate.

    [Test]
    public async Task PeerTransfer_WithPayloadAndThumbnail_CommitsToLongTerm_Disk()
    {
        // 1. Sender (Frodo) and recipient (Sam) drives with the same TargetDrive.
        // 2. Recipient circle granting DrivePermission.Write; connect.
        // 3. Frodo uploads a file WITH one payload + one thumbnail and
        //    TransitOptions { Recipients = [Sam], AllowDistribution = true }.
        // 4. await senderRedux.DriveRedux.WaitForEmptyOutbox(targetDrive);
        // 5. await recipientRedux.DriveRedux.ProcessInbox(targetDrive);
        // 6. var recvd = await recipientRedux.DriveRedux
        //        .QueryByGlobalTransitId(uploadResult.GlobalTransitIdFileIdentifier);
        // 7. Assert recvd header exists, payload + thumbnail are present and
        //    downloadable with the expected bytes.
        Assert.Fail("fill in using the _Universal peer-transfer pattern");
    }
}
```

- [ ] **Step 2: Run; expect FAIL** (placeholder `Assert.Fail`).

Run: `dotnet test ./tests/apps/Odin.Hosting.Tests/Odin.Hosting.Tests.csproj --filter "FullyQualifiedName~InboxPromotionCharacterizationTests"`
Expected: FAIL.

- [ ] **Step 3: Implement the test body** against the chosen reference class.
  Download the recipient's payload and thumbnail and assert byte-equality with
  what Frodo uploaded.

- [ ] **Step 4: Run; expect PASS** on disk (this is current behavior).

- [ ] **Step 5: Commit.**

```bash
git add tests/apps/Odin.Hosting.Tests/_Universal/DriveStorage/InboxPromotionCharacterizationTests.cs
git commit -m "test: characterize peer-transfer payload promotion on disk"
```

### Task 2: Local-upload-with-payload characterization test (disk)

**Files:**
- Test: same file as Task 1 (add a method).

The direct/local upload path also flows through `CommitNewFile` with
`sourceFolderPath = GetDriveUploadPath()`. Cover it so Phase 5's `StagingArea`
change is protected for the Upload branch too.

- [ ] **Step 1: Write the test** `LocalUpload_WithPayloadAndThumbnail_CommitsToLongTerm_Disk`
  using a single owner identity: upload a file with payload + thumbnail via
  `OwnerApiClientRedux.DriveRedux`, then query it back and assert payload +
  thumbnail bytes.
- [ ] **Step 2: Run; expect PASS** (current behavior).
- [ ] **Step 3: Commit.**

```bash
git add tests/apps/Odin.Hosting.Tests/_Universal/DriveStorage/InboxPromotionCharacterizationTests.cs
git commit -m "test: characterize local-upload payload promotion on disk"
```

---

## Phase 2 — Introduce `IDriveFileStore` + `DiskFileStore` (no behavior change)

### Task 3: Define the interface and backend enum

**Files:**
- Create: `src/services/Odin.Services/Drives/DriveCore/Storage/IDriveFileStore.cs`

- [ ] **Step 1: Write the interface.**

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Exceptions;

namespace Odin.Services.Drives.DriveCore.Storage;

#nullable enable

public enum StorageBackendType { Disk, S3 }

/// Backend-agnostic blob I/O over a path (disk) or key (S3).
public interface IDriveFileStore
{
    StorageBackendType Backend { get; }

    Task<uint>   WriteStreamAsync(string path, Stream stream, CancellationToken ct = default);
    Task         WriteBytesAsync(string path, byte[] bytes, CancellationToken ct = default);
    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken ct = default);
    Task<byte[]> ReadBytesAsync(string path, long start, long length, CancellationToken ct = default);
    Task<bool>   ExistsAsync(string path, CancellationToken ct = default);
    Task<long>   LengthAsync(string path, CancellationToken ct = default);
    Task         DeleteAsync(string path, CancellationToken ct = default);
    Task         DeleteSetAsync(string dir, Guid fileId, CancellationToken ct = default); // {fileId:N}.*
    Task         EnsureDirectoryAsync(string dir, CancellationToken ct = default);          // no-op on S3

    /// Bring a staged file from `source` into this store. Dispatches on
    /// (source.Backend, this.Backend). See PromoteAsync table in the spec.
    Task IngestFromAsync(IDriveFileStore source, string sourcePath, string destPath, CancellationToken ct = default);
}

public class DriveFileStoreException : OdinSystemException
{
    public DriveFileStoreException(string message) : base(message) { }
    public DriveFileStoreException(string message, Exception inner) : base(message, inner) { }
}
```

- [ ] **Step 2: Build.** `dotnet build ./src/services/Odin.Services/Odin.Services.csproj` → 0 errors.
- [ ] **Step 3: Commit.**

```bash
git add src/services/Odin.Services/Drives/DriveCore/Storage/IDriveFileStore.cs
git commit -m "feat: add IDriveFileStore interface + StorageBackendType"
```

### Task 4: `DiskFileStore` + unit tests

**Files:**
- Create: `src/services/Odin.Services/Drives/DriveCore/Storage/DiskFileStore.cs`
- Test: `tests/services/Odin.Services.Tests/Drives/DriveCore/Storage/DiskFileStoreTests.cs`

`DiskFileStore` wraps the existing `FileReaderWriter` (so retry/chunking
behavior is preserved). `DeleteSetAsync` carries over the inbox glob.
`IngestFromAsync` for a disk dest only supports a disk source (assert otherwise);
it uses `FileReaderWriter.CopyPayloadFile`.

- [ ] **Step 1: Write the failing tests** (mock `FileReaderWriter`? It's a
  concrete class — instead use a real `FileReaderWriter` over a temp dir, like
  `InboxFileReaderWriterTests` does). Mirror that fixture
  (`PayloadReaderWriterBaseTestFixture` for temp-dir setup, real `FileReaderWriter`
  built from an `OdinConfiguration` with `FileOperationRetryAttempts=1`,
  `FileOperationRetryDelayMs=1ms`, `FileWriteChunkSizeInBytes=4096`).

```csharp
[Test]
public async Task WriteStream_ThenRead_RoundTrips()
{
    var sut = new DiskFileStore(_fileReaderWriter);
    var path = Path.Combine(TestRootPath, "d", "f.metadata");
    await sut.EnsureDirectoryAsync(Path.GetDirectoryName(path)!);
    using var ms = new MemoryStream("hi"u8.ToArray());
    var n = await sut.WriteStreamAsync(path, ms);
    Assert.That(n, Is.EqualTo(2));
    Assert.That(await sut.ExistsAsync(path), Is.True);
    Assert.That(await sut.ReadAllBytesAsync(path), Is.EqualTo("hi"u8.ToArray()));
    Assert.That(sut.Backend, Is.EqualTo(StorageBackendType.Disk));
}

[Test]
public async Task DeleteSet_RemovesOnlyMatchingFileId()
{
    var sut = new DiskFileStore(_fileReaderWriter);
    var dir = Path.Combine(TestRootPath, "drive");
    Directory.CreateDirectory(dir);
    var keep = Guid.NewGuid(); var drop = Guid.NewGuid();
    File.WriteAllText(Path.Combine(dir, $"{drop:N}.metadata"), "x");
    File.WriteAllText(Path.Combine(dir, $"{drop:N}.p-1.payload"), "x");
    File.WriteAllText(Path.Combine(dir, $"{keep:N}.p-2.payload"), "x");
    await sut.DeleteSetAsync(dir, drop);
    Assert.That(File.Exists(Path.Combine(dir, $"{drop:N}.metadata")), Is.False);
    Assert.That(File.Exists(Path.Combine(dir, $"{keep:N}.p-2.payload")), Is.True);
}

[Test]
public async Task IngestFrom_DiskToDisk_CopiesFile()
{
    var sut = new DiskFileStore(_fileReaderWriter);
    var src = Path.Combine(TestRootPath, "src", "a.payload");
    var dst = Path.Combine(TestRootPath, "dst", "b.payload");
    await sut.EnsureDirectoryAsync(Path.GetDirectoryName(src)!);
    await sut.EnsureDirectoryAsync(Path.GetDirectoryName(dst)!);
    using (var ms = new MemoryStream("payload"u8.ToArray())) await sut.WriteStreamAsync(src, ms);
    await sut.IngestFromAsync(sut, src, dst);
    Assert.That(await sut.ReadAllBytesAsync(dst), Is.EqualTo("payload"u8.ToArray()));
}
```

- [ ] **Step 2: Run; expect FAIL** (`DiskFileStore` not defined).
- [ ] **Step 3: Implement `DiskFileStore`.**

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Odin.Services.Drives.DriveCore.Storage;

#nullable enable

public sealed class DiskFileStore(FileReaderWriter frw) : IDriveFileStore
{
    public StorageBackendType Backend => StorageBackendType.Disk;

    public Task<uint> WriteStreamAsync(string path, Stream stream, CancellationToken ct = default)
        => frw.WriteStreamAsync(path, stream);

    public async Task WriteBytesAsync(string path, byte[] bytes, CancellationToken ct = default)
        => await frw.WriteAllBytesAsync(path, bytes, ct);

    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken ct = default)
        => frw.GetAllFileBytesAsync(path);

    public Task<byte[]> ReadBytesAsync(string path, long start, long length, CancellationToken ct = default)
        => frw.GetFileBytesAsync(path, start, length, ct);

    public Task<bool> ExistsAsync(string path, CancellationToken ct = default)
        => Task.FromResult(frw.FileExists(path));

    public Task<long> LengthAsync(string path, CancellationToken ct = default)
        => Task.FromResult(new FileInfo(path).Length);

    public Task DeleteAsync(string path, CancellationToken ct = default)
    {
        frw.DeleteFile(path);
        return Task.CompletedTask;
    }

    public Task DeleteSetAsync(string dir, Guid fileId, CancellationToken ct = default)
    {
        if (Directory.Exists(dir))
            frw.DeleteFiles(Directory.GetFiles(dir, $"{fileId:N}.*"));
        return Task.CompletedTask;
    }

    public Task EnsureDirectoryAsync(string dir, CancellationToken ct = default)
    {
        frw.CreateDirectory(dir);
        return Task.CompletedTask;
    }

    public Task IngestFromAsync(IDriveFileStore source, string sourcePath, string destPath, CancellationToken ct = default)
    {
        if (source.Backend != StorageBackendType.Disk)
            throw new DriveFileStoreException($"Disk dest cannot ingest from {source.Backend} source");
        frw.CopyPayloadFile(sourcePath, destPath);
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 4: Run; expect PASS.**

Run: `dotnet test ./tests/services/Odin.Services.Tests/Odin.Services.Tests.csproj --filter "FullyQualifiedName~DiskFileStoreTests"`

- [ ] **Step 5: Commit.**

```bash
git add src/services/Odin.Services/Drives/DriveCore/Storage/DiskFileStore.cs tests/services/Odin.Services.Tests/Drives/DriveCore/Storage/DiskFileStoreTests.cs
git commit -m "feat: DiskFileStore over FileReaderWriter + tests"
```

### Task 5: Route the three area managers through stores (disk instances) via DI

This is the behavior-preserving switch: the managers keep their public API but
obtain an `IDriveFileStore` instead of touching `FileReaderWriter` /
`IInboxReaderWriter` / `IPayloadReaderWriter` directly. Because everything is
still disk, the characterization tests stay green.

**Files:**
- Modify: `…/Storage/InboxStorageManager.cs`, `…/Storage/UploadStorageManager.cs`,
  `…/Storage/LongTermStorageManager.cs` (only its blob calls)
- Modify: `src/apps/Odin.Hosting/TenantServices.cs` (register named stores)

Named-store DI is the crux. Register three keyed/named `IDriveFileStore`
instances and inject each manager with the right one. Autofac supports named
registrations; use `.Named<IDriveFileStore>("upload" | "inbox" | "longterm")`
and `ResolveNamed`, or a small wrapper type per area
(`UploadFileStore : IDriveFileStore` delegating) to keep constructor injection
simple. **Recommended:** wrapper types, because Autofac constructor injection by
name is awkward and the wrapper makes the binding explicit:

- [ ] **Step 1:** Create three thin wrapper classes in `…/Storage/`:
  `UploadFileStore`, `InboxFileStore`, `LongTermPayloadStore`, each
  `: IDriveFileStore` holding an inner `IDriveFileStore` and forwarding every
  member. In Phase 2 all three are constructed wrapping a `DiskFileStore`.

```csharp
// Example; the other two are identical with their own class name.
public sealed class InboxFileStore(IDriveFileStore inner) : IDriveFileStore
{
    public StorageBackendType Backend => inner.Backend;
    public Task<uint> WriteStreamAsync(string p, Stream s, CancellationToken ct = default) => inner.WriteStreamAsync(p, s, ct);
    public Task WriteBytesAsync(string p, byte[] b, CancellationToken ct = default) => inner.WriteBytesAsync(p, b, ct);
    public Task<byte[]> ReadAllBytesAsync(string p, CancellationToken ct = default) => inner.ReadAllBytesAsync(p, ct);
    public Task<byte[]> ReadBytesAsync(string p, long s, long l, CancellationToken ct = default) => inner.ReadBytesAsync(p, s, l, ct);
    public Task<bool> ExistsAsync(string p, CancellationToken ct = default) => inner.ExistsAsync(p, ct);
    public Task<long> LengthAsync(string p, CancellationToken ct = default) => inner.LengthAsync(p, ct);
    public Task DeleteAsync(string p, CancellationToken ct = default) => inner.DeleteAsync(p, ct);
    public Task DeleteSetAsync(string d, Guid f, CancellationToken ct = default) => inner.DeleteSetAsync(d, f, ct);
    public Task EnsureDirectoryAsync(string d, CancellationToken ct = default) => inner.EnsureDirectoryAsync(d, ct);
    public Task IngestFromAsync(IDriveFileStore src, string s, string d, CancellationToken ct = default) => inner.IngestFromAsync(src, s, d, ct);
}
```

- [ ] **Step 2:** In `TenantServices.cs`, register (Phase 2 — disk for all three):

```csharp
cb.Register(c => new UploadFileStore(new DiskFileStore(c.Resolve<FileReaderWriter>())))
  .AsSelf().SingleInstance();
cb.Register(c => new InboxFileStore(new DiskFileStore(c.Resolve<FileReaderWriter>())))
  .AsSelf().SingleInstance();
cb.Register(c => new LongTermPayloadStore(new DiskFileStore(c.Resolve<FileReaderWriter>())))
  .AsSelf().SingleInstance();
```

- [ ] **Step 3:** Change `InboxStorageManager` to depend on `InboxFileStore` and
  delegate (`WriteInboxStream` → `EnsureDirectoryAsync` + `WriteStreamAsync`;
  `GetAllInboxFileBytes` → `ReadAllBytesAsync`; `InboxFileExists` →
  `ExistsAsync`; `CleanupInboxFiles` → `DeleteSetAsync`). Same for
  `UploadStorageManager` → `UploadFileStore`. For `LongTermStorageManager`,
  change **only** the payload-blob methods to use `LongTermPayloadStore` (leave
  header/DB methods untouched). Keep public signatures identical.

- [ ] **Step 4:** Build, then run **all three** existing storage test classes
  plus the Phase 1 characterization tests.

Run:
```
dotnet test ./tests/services/Odin.Services.Tests/Odin.Services.Tests.csproj --filter "FullyQualifiedName~Storage"
dotnet test ./tests/apps/Odin.Hosting.Tests/Odin.Hosting.Tests.csproj --filter "FullyQualifiedName~InboxPromotionCharacterizationTests"
```
Expected: PASS (no behavior change).

- [ ] **Step 5: Commit.**

```bash
git add -A
git commit -m "refactor: route inbox/upload/longterm managers through IDriveFileStore (disk)"
```

---

## Phase 3 — `S3FileStore` (switch inbox + payload to S3 under the flags)

### Task 6: `S3FileStore` + unit tests (mock + MinIO)

**Files:**
- Create: `src/services/Odin.Services/Drives/DriveCore/Storage/S3FileStore.cs`
- Test (ungated, mock): `tests/services/Odin.Services.Tests/Drives/DriveCore/Storage/S3FileStoreUnitTests.cs`
- Test (gated, MinIO): add a `#if RUN_S3_TESTS` class to the same file, mirroring
  the MinIO harness already in `InboxS3ReaderWriterTests.cs`.

`S3FileStore` wraps an `IS3Storage` instance and folds in the retry policy +
`DriveFileStoreException` wrapping (copy the predicate from
`InboxS3ReaderWriter.CreateRetry`: retry timeout/5xx outer + inner
`HttpErrorResponseException` 5xx, not 4xx). `IngestFromAsync` for an S3 dest:

- source disk → `s3.UploadFileAsync(sourcePath /*local*/, destPath /*key*/)`.
- source S3 → cross-bucket copy (Task 7's new primitive).

- [ ] **Step 1: Write mock unit tests** mirroring `InboxS3ReaderWriterUnitTests`
  (use `Mock<IS3Storage>`; assert delegation, the retry classification via the
  cancel-on-invoke probe, `DriveFileStoreException` wrapping, OCE passthrough,
  and `Backend == S3`). Reuse the exact patterns in that file.
- [ ] **Step 2: Run; expect FAIL.**
- [ ] **Step 3: Implement `S3FileStore`** mapping each member to `IS3Storage`
  (`WriteStreamAsync→WriteStreamAsync` cast to `uint`, `WriteBytesAsync→WriteBytesAsync`,
  `ReadAllBytesAsync→ReadBytesAsync(path)`, `ReadBytesAsync→ReadBytesAsync(path,start,length)`,
  `ExistsAsync→FileExistsAsync`, `LengthAsync→FileLengthAsync`,
  `DeleteAsync→DeleteFileAsync`, `DeleteSetAsync→DeleteByPrefixAsync(S3Path.Combine(dir, $"{fileId:N}."))`,
  `EnsureDirectoryAsync→no-op`), all wrapped in the retry + `DriveFileStoreException`.
  `IngestFromAsync` as above (S3-source branch calls the Task 7 primitive).
- [ ] **Step 4: Run mock tests; expect PASS.** Compile-check gated tests with
  `USER=seb dotnet build …`.
- [ ] **Step 5: Commit.**

```bash
git add src/services/Odin.Services/Drives/DriveCore/Storage/S3FileStore.cs tests/services/Odin.Services.Tests/Drives/DriveCore/Storage/S3FileStoreUnitTests.cs
git commit -m "feat: S3FileStore over IS3Storage with retry + tests"
```

### Task 7: Cross-bucket S3 copy primitive

**Files:**
- Modify: `src/core/Odin.Core.Storage/ObjectStorage/IS3Storage.cs` (add method)
- Modify: `src/core/Odin.Core.Storage/ObjectStorage/S3AwsStorage.cs` (implement)
- Test (gated): `tests/.../S3AwsStorageTests` or the existing S3 storage test class.

The inbox bucket and payload bucket differ (separate buckets, locked decision),
so the `S3→S3` promote needs a copy whose **source bucket** differs from the
destination store's bucket. Existing `CopyFileAsync` uses `BucketName` for both.

- [ ] **Step 1: Add to `IS3Storage`:**

```csharp
// Server-side copy of an object from a different bucket into this store.
Task CopyFromBucketAsync(string sourceBucket, string sourceKey, string destKey,
                         CancellationToken cancellationToken = default);
```

- [ ] **Step 2: Implement on `S3AwsStorage`** using `CopyObjectRequest`
  (`SourceBucket = sourceBucket`, `SourceKey = sourceKey`,
  `DestinationBucket = BucketName`, `DestinationKey = S3Path.Combine(_rootPath, destKey)`).
  Note: `sourceKey` is already the source store's full key (its own rootPath
  applied by the caller) — pass it raw; only the **dest** key gets this store's
  `_rootPath`. Wrap failures via the existing `CreateS3StorageException`.
- [ ] **Step 3: Gated MinIO test** (two buckets): put an object in bucket A,
  `CopyFromBucketAsync` into bucket B, assert it exists in B with identical
  bytes and remains in A. `USER=seb dotnet build` to compile-check.
- [ ] **Step 4: Commit.**

```bash
git add src/core/Odin.Core.Storage/ObjectStorage/IS3Storage.cs src/core/Odin.Core.Storage/ObjectStorage/S3AwsStorage.cs tests/...
git commit -m "feat: S3AwsStorage cross-bucket CopyFromBucketAsync"
```

Then wire the `S3FileStore.IngestFromAsync` S3-source branch:
`((S3FileStore)source).Bucket` (expose the inner `IS3Storage.BucketName`) →
`this.s3.CopyFromBucketAsync(sourceBucket, sourcePath, destPath)`.

### Task 8: Select store backend by config in DI

**Files:**
- Modify: `src/apps/Odin.Hosting/TenantServices.cs`

- [ ] **Step 1:** Change the three store registrations from Task 5 so inbox and
  long-term payload pick S3 when enabled (upload stays disk):

```csharp
// Upload: always disk
cb.Register(c => new UploadFileStore(new DiskFileStore(c.Resolve<FileReaderWriter>())))
  .AsSelf().SingleInstance();

// Inbox: S3 when S3Inbox.Enabled, else disk
cb.Register(c => new InboxFileStore(
        odinConfig.S3Inbox.Enabled
            ? new S3FileStore(c.Resolve<IS3InboxStorage>(), c.Resolve<ILogger<S3FileStore>>(), odinConfig)
            : new DiskFileStore(c.Resolve<FileReaderWriter>())))
  .AsSelf().SingleInstance();

// Long-term payload: S3 when S3Storage.Enabled (today's payload key), else disk
cb.Register(c => new LongTermPayloadStore(
        odinConfig.S3Storage.Enabled
            ? new S3FileStore(c.Resolve<IS3PayloadStorage>(), c.Resolve<ILogger<S3FileStore>>(), odinConfig)
            : new DiskFileStore(c.Resolve<FileReaderWriter>())))
  .AsSelf().SingleInstance();
```

  (Note the *current* selection keys: inbox by `S3Inbox.Enabled`, payload by
  `S3Storage.Enabled`. Phase 6 collapses both to one switch.)

- [ ] **Step 2: Build the host.**

Run: `dotnet build ./src/apps/Odin.Hosting/Odin.Hosting.csproj -p:StaticWebAssetsEnabled=false`
Expected: 0 errors. (The static-web-assets target can fail on a stale cache path
unrelated to C#; the flag skips it.)

- [ ] **Step 3:** Disk-default tests still green:
  `dotnet test … --filter "FullyQualifiedName~InboxPromotionCharacterizationTests"`.
- [ ] **Step 4: Commit.**

```bash
git add src/apps/Odin.Hosting/TenantServices.cs
git commit -m "feat: select inbox/payload store backend by config"
```

---

## Phase 4 — The promote fix (this is the behavior change)

### Task 9: Route payload/thumbnail promotion through `IngestFromAsync`

**Files:**
- Modify: `…/DriveCore/Storage/LongTermStorageManager.cs:374` (`CopyPayloadToLongTermAsync`) and `:387` (`CopyThumbnailToLongTermAsync`)
- Modify: `…/FileSystem/Base/DriveStorageServiceBase.cs:1739` (`CopyPayloadAndThumbnailsToLongTermStorage`) to pass the **source store**

Currently `CopyPayloadToLongTermAsync(drive, targetFileId, descriptor, sourceFile)`
calls `payloadReaderWriter.CopyPayloadFileAsync(sourceFile, dest)` (disk-source
assumption). Change it to take the **source `IDriveFileStore`** and call
`longTermPayloadStore.IngestFromAsync(sourceStore, sourceFile, dest)`.

`CopyPayloadAndThumbnailsToLongTermStorage` already receives `sourceFolderPath`;
until Phase 5 it must resolve that to the source store. Use the same inbox-path
check as the interim metadata fix: if `sourceFolderPath` equals
`drive.GetDriveInboxPath()` the source store is `InboxFileStore`, else
`UploadFileStore`. (Phase 5 replaces this with the resolved `StagingArea`.)

- [ ] **Step 1: Write a gated MinIO integration test** that reproduces the
  original failure: stage a payload + thumbnail under an **S3** inbox prefix,
  then promote and assert the long-term S3 payload exists with identical bytes.
  Put it beside the `S3FileStore` MinIO tests. (Cannot run in sandbox; compile
  with `USER=seb dotnet build`.)
- [ ] **Step 2:** Change `CopyPayloadToLongTermAsync` / `CopyThumbnailToLongTermAsync`
  signatures to accept `IDriveFileStore sourceStore` and call
  `_longTermPayloadStore.IngestFromAsync(sourceStore, sourceFile, destinationFile, ct)`
  (the dest store provides the dest path build as today).
- [ ] **Step 3:** In `CopyPayloadAndThumbnailsToLongTermStorage`, resolve the
  source store from `sourceFolderPath` (inbox-path check) and thread it into the
  two calls.
- [ ] **Step 4:** Disk characterization tests (Phase 1) **must still pass** —
  this is the critical regression gate.

Run: `dotnet test ./tests/apps/Odin.Hosting.Tests/Odin.Hosting.Tests.csproj --filter "FullyQualifiedName~InboxPromotionCharacterizationTests"`
Expected: PASS (disk→disk and the disk-upload→disk paths unchanged).

- [ ] **Step 5: Commit.**

```bash
git add -A
git commit -m "fix: promote staged payloads via IngestFromAsync (source-backend aware)"
```

### Task 10: Route the staged metadata read through the resolved source store

**Files:**
- Modify: `…/FileSystem/Base/DriveStorageServiceBase.cs` `GetAllFileBytesFromTempFileForWriting`

The interim fix already routes the **inbox** branch through
`inboxStorageManager.GetAllInboxFileBytes`. Make it use the stores directly for
consistency with the promote: inbox-path → `inboxFileStore.ReadAllBytesAsync`,
else `uploadFileStore.ReadAllBytesAsync`. Behavior identical on disk.

- [ ] **Step 1:** Replace the body to resolve the `(inbox|upload)` store by the
  same path check and read via `store.ReadAllBytesAsync(key)`.

  **Path/key note (important):** for disk, `key = Path.Combine(sourceFolderPath,
  TenantPathManager.GetFilename(file.FileId, extension))`. For S3 the inbox store
  applies its own `rootPath`, and `sourceFolderPath` for inbox is the
  S3-relative prefix from `GetDriveInboxPath`. These must produce the **same key**
  that `InboxStorageManager.GetAllInboxFileBytes` uses (it builds
  `GetDriveInboxFilePath(driveId, fileId, ext)`). Verify they match; if they
  don't, do **not** rebuild the key here — delegate the inbox branch to
  `inboxStorageManager.GetAllInboxFileBytes(file, extension)` (the path already
  proven correct by the interim fix) and only use the store directly for the
  upload branch.
- [ ] **Step 2:** Phase 1 tests pass on disk; build host.
- [ ] **Step 3: Commit.**

```bash
git add src/services/Odin.Services/Drives/FileSystem/Base/DriveStorageServiceBase.cs
git commit -m "refactor: staged metadata read via resolved source store"
```

---

## Phase 5 — Replace `sourceFolderPath` with `StagingArea`

### Task 11: Introduce the enum and resolution point

**Files:**
- Create: `…/Drives/DriveCore/Storage/StagingArea.cs` (`enum StagingArea { Upload, Inbox }`)
- Modify: `…/FileSystem/Base/DriveStorageServiceBase.cs` (add a private resolver)

- [ ] **Step 1:** Add `public enum StagingArea { Upload, Inbox }`.
- [ ] **Step 2:** Add to `DriveStorageServiceBase`:

```csharp
private IDriveFileStore ResolveStore(StagingArea area) => area switch
{
    StagingArea.Upload => uploadFileStore,
    StagingArea.Inbox  => inboxFileStore,
    _ => throw new ArgumentOutOfRangeException(nameof(area))
};

private static string StagingRoot(StagingArea area, StorageDrive drive) => area switch
{
    StagingArea.Upload => drive.GetDriveUploadPath(),
    StagingArea.Inbox  => drive.GetDriveInboxPath(),
    _ => throw new ArgumentOutOfRangeException(nameof(area))
};
```

  (Inject `UploadFileStore uploadFileStore`, `InboxFileStore inboxFileStore`,
  `LongTermPayloadStore longTermPayloadStore` into the ctor.)

- [ ] **Step 3: Commit.**

```bash
git add -A
git commit -m "feat: StagingArea enum + store resolver"
```

### Task 12: Thread `StagingArea` through the pipeline, delete the path-compare

**Files (change `string sourceFolderPath` → `StagingArea sourceArea`):**
- `…/Base/DriveStorageServiceBase.cs`: `GetAllFileBytesFromTempFileForWriting`,
  `CommitNewFile`, `OverwriteFile`, `UpdateBatchAsync`,
  `CopyPayloadsAndThumbnailsToLongTermStorage`, `CopyPayloadAndThumbnailsToLongTermStorage`.
- `…/Peer/Incoming/Drive/Transfer/PeerFileWriter.cs` (params + internal calls)
- `…/Peer/Incoming/Drive/Transfer/FileUpdate/PeerFileUpdateWriter.cs`
- Call sites: `PeerInboxProcessor.cs:343,365,387,457`,
  `PeerDriveIncomingTransferService.cs:381,397`,
  `PeerDriveIncomingFileUpdateService.cs:193`, `StandardFileStreamWriter.cs:64,94`,
  `CommentStreamWriter.cs:75,109`, `FileSystemUpdateWriterBase.cs:286,342`.

- [ ] **Step 1:** At each call site replace `sourceFolderPath: drive.GetDriveUploadPath()`
  with `sourceArea: StagingArea.Upload` and `…GetDriveInboxPath()` with
  `sourceArea: StagingArea.Inbox`. (The `IsDirectWrite ? … : …` ternaries become
  `IsDirectWrite ? StagingArea.Upload : StagingArea.Inbox`.)
- [ ] **Step 2:** Inside the pipeline, build paths from
  `StagingRoot(area, drive)` and read/promote via `ResolveStore(area)`. Delete
  the interim inbox-path string comparison (it's now dead).
- [ ] **Step 3: Build (host + services).** Fix all compile breaks (the compiler
  enumerates every site — there are no hidden ones).
- [ ] **Step 4:** Phase 1 characterization tests pass on disk; full storage test
  suite passes.
- [ ] **Step 5: Commit.**

```bash
git add -A
git commit -m "refactor: replace sourceFolderPath string with StagingArea enum"
```

---

## Phase 6 — Collapse config + delete the old interfaces

### Task 13: Single S3 switch

**Files:**
- Modify: `…/Configuration/OdinConfiguration.cs`, `SystemServices.cs`, `TenantServices.cs`

- [ ] **Step 1:** Introduce one effective switch. Keep `S3Storage` (connection)
  but make "S3 on" mean inbox **and** payload on S3. Simplest:
  treat `S3Storage.Enabled` as the master and require both `S3Payload` and
  `S3Inbox` buckets when it's on; drive all three store registrations and the
  `AddS3Aws*Storage` calls off `S3Storage.Enabled` (with a validation that both
  bucket names are present). Remove the independent `Enabled` flags' effect on
  selection (keep the bucket/rootPath settings).
- [ ] **Step 2:** Update the `TenantServices` store registrations (Task 8) to key
  all backend selection off the single switch.
- [ ] **Step 3:** Update/مigrate the config tests in
  `OdinConfiguration`/`S3*Section` tests; update sample `appsettings`.
- [ ] **Step 4: Build + full suite green on disk.**
- [ ] **Step 5: Commit.**

```bash
git add -A
git commit -m "refactor: collapse S3 inbox/payload toggles into one switch"
```

### Task 14: Delete the old interfaces and implementations

**Files (delete):**
- `IInboxReaderWriter.cs` (keep `InboxReaderWriterException`? No — fold callers
  onto `DriveFileStoreException`), `InboxFileReaderWriter.cs`,
  `InboxS3ReaderWriter.cs`, `IPayloadReaderWriter.cs`,
  `PayloadFileReaderWriter.cs`, `PayloadS3ReaderWriter.cs`.
- Their tests: `InboxFileReaderWriterTests.cs`, `InboxStorageManagerTests.cs`
  (keep — it tests the manager, which survives; update its construction to the
  store), `InboxS3ReaderWriterTests.cs` (the unit + MinIO inbox-reader tests are
  superseded by `S3FileStore` tests — delete).

- [ ] **Step 1:** Remove the DI registrations for `IInboxReaderWriter` /
  `IPayloadReaderWriter` in `TenantServices.cs`.
- [ ] **Step 2:** Delete the six source files. Build; the compiler lists every
  remaining reference. Re-point each onto the relevant store
  (`InboxFileStore`/`LongTermPayloadStore`) or its manager.
- [ ] **Step 3:** Delete/retarget the superseded tests; keep manager tests,
  updating their construction to inject the disk store.
- [ ] **Step 4: Full build + full storage suite + Phase 1 characterization green.**
- [ ] **Step 5: Commit.**

```bash
git add -A
git commit -m "refactor: delete IInboxReaderWriter/IPayloadReaderWriter and impls"
```

---

## Final verification

- [ ] `dotnet build ./odin-core.sln` → 0 errors.
- [ ] `dotnet test ./tests/services/Odin.Services.Tests/Odin.Services.Tests.csproj --filter "FullyQualifiedName~Storage"` → all green.
- [ ] `dotnet test ./tests/apps/Odin.Hosting.Tests/Odin.Hosting.Tests.csproj --filter "FullyQualifiedName~InboxPromotionCharacterizationTests"` → green (disk).
- [ ] `USER=seb dotnet build ./tests/services/Odin.Services.Tests/Odin.Services.Tests.csproj` → gated S3 tests compile.
- [ ] In CI / a MinIO env: enable S3 (inbox + payload), run a peer transfer **with
  a payload + thumbnail**, confirm it processes, commits to long-term S3, and the
  inbox staging is cleaned. This is the scenario that originally failed.
- [ ] Grep confirms `sourceFolderPath`, `IInboxReaderWriter`, `IPayloadReaderWriter`
  are gone: `rg "sourceFolderPath|IInboxReaderWriter|IPayloadReaderWriter" src/` → no hits.
