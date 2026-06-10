# S3 Inbox Storage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make peer-transfer inbox staging optionally back onto S3 instead of local disk, selected by configuration, mirroring the existing payload storage pattern, with node-independent staging for multi-node hosting.

**Architecture:** Introduce an `IInboxReaderWriter` abstraction (disk + S3 implementations) that `InboxStorageManager` depends on instead of touching `FileReaderWriter` directly. `TenantPathManager` anchors inbox paths to the bucket root when S3 inbox is enabled, exactly as it already does for payloads. New S3 capabilities (`DeleteByPrefixAsync`, `EnsureExpirationLifecycleAsync`) go on the base `S3AwsStorage` class. A configurable, default-off lifecycle expiration rule backstops orphaned staging.

**Tech Stack:** .NET 9, Autofac DI, AWSSDK.S3 4.0.17, NUnit, Testcontainers.Minio (S3 tests behind `#if RUN_S3_TESTS`, require Docker — CI only).

**Spec:** `docs/superpowers/specs/2026-05-27-s3-inbox-storage-design.md`

---

## Notes for the implementer

- **S3 tests require Docker** (Testcontainers spins up MinIO) and only compile under the `RUN_S3_TESTS` constant. They run in CI, not in the sandbox. When a step says "run the S3 test," that means in an environment with Docker + `RUN_S3_TESTS` defined. Disk-backed tests run anywhere.
- **The base-class rule:** `DeleteByPrefixAsync` and `EnsureExpirationLifecycleAsync` are declared on `IS3Storage` and implemented on `S3AwsStorage`. Do **not** add members to `S3AwsInboxStorage` — it stays an empty marker subclass; it inherits everything.
- Existing field names in `S3AwsStorage` used below: `_s3Client` (IAmazonS3), `BucketName` (property), `_rootPath` (string), and the helper `CreateS3StorageException(Exception, string)`. Confirm these by reading `src/core/Odin.Core.Storage/ObjectStorage/S3AwsStorage.cs` before editing.

---

## Task 1: `DeleteByPrefixAsync` on base S3 storage

Cleanup of an inbox file set needs prefix-based deletion (`<driveId>/<fileId:N>.`). `DeleteDirectoryAsync` already does paginated list+delete; extract the loop and add a raw-prefix entry point.

**Files:**
- Modify: `src/core/Odin.Core.Storage/ObjectStorage/IS3Storage.cs`
- Modify: `src/core/Odin.Core.Storage/ObjectStorage/S3AwsStorage.cs:266-308`
- Test: `tests/core/Odin.Core.Storage.Tests/ObjectStorage/S3AwsStorageTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `S3AwsStorageTests.cs` (inside the `#if RUN_S3_TESTS` class, mirror the existing `S3AwsStorage_ItShouldDeleteADirectory` style):

```csharp
[Test]
[TestCase("")]
[TestCase("inbox")]
public async Task S3AwsStorage_ItShouldDeleteByPrefix(string root)
{
    const string text = "test";
    var bucket = new S3AwsStorage(_logger, _s3Client, _bucketName, root);

    var driveDir = "drives/aaaa";
    var fileId = "1234567890abcdef";
    var otherFileId = "fedcba0987654321";

    // Files for the target fileId (share the "driveDir/fileId." prefix)
    var targetKeys = new[]
    {
        $"{driveDir}/{fileId}.metadata",
        $"{driveDir}/{fileId}.transferkeyheader",
        $"{driveDir}/{fileId}.foo-1.payload",
        $"{driveDir}/{fileId}.foo-1-320x320.thumb",
    };
    // A sibling fileId in the same dir that must survive
    var survivorKey = $"{driveDir}/{otherFileId}.foo-2.payload";

    foreach (var k in targetKeys)
        await bucket.WriteBytesAsync(k, System.Text.Encoding.UTF8.GetBytes(text));
    await bucket.WriteBytesAsync(survivorKey, System.Text.Encoding.UTF8.GetBytes(text));

    await bucket.DeleteByPrefixAsync($"{driveDir}/{fileId}."); // should not throw on a fresh prefix either

    foreach (var k in targetKeys)
        Assert.That(await bucket.FileExistsAsync(k), Is.False, $"{k} should be deleted");
    Assert.That(await bucket.FileExistsAsync(survivorKey), Is.True, "sibling fileId must survive");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run (Docker + RUN_S3_TESTS):
```bash
dotnet test tests/core/Odin.Core.Storage.Tests/Odin.Core.Storage.Tests.csproj \
  --filter "FullyQualifiedName~S3AwsStorage_ItShouldDeleteByPrefix"
```
Expected: FAIL to compile — `DeleteByPrefixAsync` not defined.

- [ ] **Step 3: Add the interface method**

In `IS3Storage.cs`, next to `DeleteDirectoryAsync`:
```csharp
Task DeleteByPrefixAsync(string prefix, CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Refactor `DeleteDirectoryAsync` and add `DeleteByPrefixAsync`**

Replace the body of `DeleteDirectoryAsync` (currently `S3AwsStorage.cs:266-308`) with:

```csharp
// SEB:NOTE this will not delete versioned objects (if versioning is enabled on the bucket).
public async Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken = default)
{
    S3Path.AssertFolderName(path);
    path = S3Path.Combine(_rootPath, path);
    await DeletePrefixInternalAsync(path, cancellationToken);
}

// Delete every object whose key starts with the given (non-folder) prefix.
// Used by inbox cleanup to remove all "{fileId:N}." staging objects for one fileId.
public async Task DeleteByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
{
    prefix = S3Path.Combine(_rootPath, prefix);
    await DeletePrefixInternalAsync(prefix, cancellationToken);
}

private async Task DeletePrefixInternalAsync(string prefix, CancellationToken cancellationToken)
{
    // S3 doesn't have directories, so we list and delete all objects with the given prefix.
    try
    {
        bool isTruncated;
        string? continuationToken = null;
        do
        {
            var listResponse = await _s3Client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = BucketName,
                ContinuationToken = continuationToken,
                MaxKeys = 1000,
                Prefix = prefix,
            }, cancellationToken);

            if (listResponse.S3Objects?.Count > 0)
            {
                var deleteRequest = new DeleteObjectsRequest
                {
                    BucketName = BucketName,
                    Objects = listResponse.S3Objects
                        .Select(o => new KeyVersion { Key = o.Key })
                        .ToList()
                };
                await _s3Client.DeleteObjectsAsync(deleteRequest, cancellationToken);
            }

            continuationToken = listResponse.NextContinuationToken;
            isTruncated = listResponse.IsTruncated ?? false;
        }
        while (isTruncated);
    }
    catch (Exception ex)
    {
        throw CreateS3StorageException(ex, $"Failed delete all objects from '{prefix}' in bucket '{BucketName}'.");
    }
}
```

Note: `S3Path.Combine` trims trailing slashes only when the last segment lacks one; our prefix ends in `.` so it is preserved verbatim. The prefix is intentionally **not** a folder name (no trailing slash), which is why `DeleteByPrefixAsync` skips `AssertFolderName`.

- [ ] **Step 5: Run test to verify it passes**

Run the Step 2 command. Expected: PASS for both `TestCase("")` and `TestCase("inbox")`.

- [ ] **Step 6: Commit**

```bash
git add src/core/Odin.Core.Storage/ObjectStorage/IS3Storage.cs \
        src/core/Odin.Core.Storage/ObjectStorage/S3AwsStorage.cs \
        tests/core/Odin.Core.Storage.Tests/ObjectStorage/S3AwsStorageTests.cs
git commit -m "Add IS3Storage.DeleteByPrefixAsync on base S3AwsStorage"
```

---

## Task 2: `EnsureExpirationLifecycleAsync` on base S3 storage

Configurable orphan backstop: ensure (or remove) an S3 lifecycle expiration rule scoped to this storage's `_rootPath` prefix.

**Files:**
- Modify: `src/core/Odin.Core.Storage/ObjectStorage/IS3Storage.cs`
- Modify: `src/core/Odin.Core.Storage/ObjectStorage/S3AwsStorage.cs`
- Test: `tests/core/Odin.Core.Storage.Tests/ObjectStorage/S3AwsStorageTests.cs`

> **Verify SDK member names first.** Open `S3AwsStorage.cs`, add `using Amazon.S3.Model;` if missing, and confirm against AWSSDK.S3 4.0.17 IntelliSense the exact names used below: `PutLifecycleConfigurationRequest`, `LifecycleConfiguration`, `LifecycleRule`, `LifecycleRuleStatus.Enabled`, `LifecycleFilter`, `LifecyclePrefixPredicate`, `LifecycleRuleExpiration.Days`, `GetLifecycleConfigurationAsync`, `DeleteLifecycleConfigurationAsync`. These are the canonical v3/v4 names; adjust only if IntelliSense disagrees.

- [ ] **Step 1: Write the failing test**

Add to `S3AwsStorageTests.cs`:

```csharp
[Test]
public async Task S3AwsStorage_ItShouldReconcileExpirationLifecycle()
{
    var bucket = new S3AwsStorage(_logger, _s3Client, _bucketName, "inbox");

    // days = 0 -> no rule
    await bucket.EnsureExpirationLifecycleAsync(0);
    var rules = await GetLifecycleRuleDaysAsync(_bucketName);
    Assert.That(rules, Is.Empty, "no expiration rule expected when days = 0");

    // days = 7 -> rule present with 7-day expiration on the "inbox/" prefix
    await bucket.EnsureExpirationLifecycleAsync(7);
    rules = await GetLifecycleRuleDaysAsync(_bucketName);
    Assert.That(rules, Does.Contain(7));

    // days = 3 -> rule updated
    await bucket.EnsureExpirationLifecycleAsync(3);
    rules = await GetLifecycleRuleDaysAsync(_bucketName);
    Assert.That(rules, Does.Contain(3));
    Assert.That(rules, Does.Not.Contain(7));

    // days = 0 -> rule removed
    await bucket.EnsureExpirationLifecycleAsync(0);
    rules = await GetLifecycleRuleDaysAsync(_bucketName);
    Assert.That(rules, Is.Empty, "rule should be removed when days returns to 0");
}

// Helper: read back the expiration-day values currently configured (empty if none).
private async Task<System.Collections.Generic.List<int>> GetLifecycleRuleDaysAsync(string bucketName)
{
    try
    {
        var resp = await _s3Client.GetLifecycleConfigurationAsync(
            new GetLifecycleConfigurationRequest { BucketName = bucketName });
        return resp.Configuration?.Rules?
            .Where(r => r.Expiration?.Days != null)
            .Select(r => r.Expiration.Days!.Value)
            .ToList() ?? new System.Collections.Generic.List<int>();
    }
    catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        return new System.Collections.Generic.List<int>();
    }
}
```

Add `using Amazon.S3.Model;` to the test file if not present.

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test tests/core/Odin.Core.Storage.Tests/Odin.Core.Storage.Tests.csproj \
  --filter "FullyQualifiedName~S3AwsStorage_ItShouldReconcileExpirationLifecycle"
```
Expected: FAIL to compile — `EnsureExpirationLifecycleAsync` not defined.

- [ ] **Step 3: Add the interface method**

In `IS3Storage.cs`:
```csharp
// Ensure (days > 0) or remove (days <= 0) an S3 lifecycle expiration rule scoped to this
// storage's root prefix. Idempotent.
Task EnsureExpirationLifecycleAsync(int expirationDays, CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Implement on `S3AwsStorage`**

Add (uses a stable rule id so reconciliation is idempotent; scopes the rule to `_rootPath` so a shared bucket is not affected outside the inbox prefix):

```csharp
private const string ExpirationLifecycleRuleId = "odin-inbox-expiration";

public async Task EnsureExpirationLifecycleAsync(int expirationDays, CancellationToken cancellationToken = default)
{
    try
    {
        // Read existing rules (treat "no config" as empty).
        List<LifecycleRule> rules;
        try
        {
            var existing = await _s3Client.GetLifecycleConfigurationAsync(
                new GetLifecycleConfigurationRequest { BucketName = BucketName }, cancellationToken);
            rules = existing.Configuration?.Rules ?? new List<LifecycleRule>();
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            rules = new List<LifecycleRule>();
        }

        // Drop any rule we own; we will re-add it if needed.
        rules = rules.Where(r => r.Id != ExpirationLifecycleRuleId).ToList();

        if (expirationDays > 0)
        {
            var prefix = string.IsNullOrEmpty(_rootPath) ? "" : _rootPath.TrimEnd('/') + "/";
            rules.Add(new LifecycleRule
            {
                Id = ExpirationLifecycleRuleId,
                Status = LifecycleRuleStatus.Enabled,
                Filter = new LifecycleFilter
                {
                    LifecycleFilterPredicate = new LifecyclePrefixPredicate { Prefix = prefix }
                },
                Expiration = new LifecycleRuleExpiration { Days = expirationDays }
            });
        }

        if (rules.Count == 0)
        {
            // No rules left at all -> remove the whole configuration.
            await _s3Client.DeleteLifecycleConfigurationAsync(
                new DeleteLifecycleConfigurationRequest { BucketName = BucketName }, cancellationToken);
        }
        else
        {
            await _s3Client.PutLifecycleConfigurationAsync(new PutLifecycleConfigurationRequest
            {
                BucketName = BucketName,
                Configuration = new LifecycleConfiguration { Rules = rules }
            }, cancellationToken);
        }
    }
    catch (Exception ex)
    {
        throw CreateS3StorageException(ex, $"Failed to reconcile expiration lifecycle on bucket '{BucketName}'.");
    }
}
```

Add `using System.Collections.Generic;`, `using System.Linq;`, `using Amazon.S3.Model;` to `S3AwsStorage.cs` if not already present.

- [ ] **Step 5: Run test to verify it passes**

Run the Step 2 command. Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/core/Odin.Core.Storage/ObjectStorage/IS3Storage.cs \
        src/core/Odin.Core.Storage/ObjectStorage/S3AwsStorage.cs \
        tests/core/Odin.Core.Storage.Tests/ObjectStorage/S3AwsStorageTests.cs
git commit -m "Add IS3Storage.EnsureExpirationLifecycleAsync (configurable, default off)"
```

---

## Task 3: `S3Inbox:ExpirationDays` config

**Files:**
- Modify: `src/services/Odin.Services/Configuration/OdinConfiguration.cs:593-617` (`S3InboxSection`)
- Modify: `src/apps/Odin.Hosting/appsettings.json` (and any `appsettings.*.json` that defines `S3Inbox`)

- [ ] **Step 1: Add the property**

In `S3InboxSection` add an `ExpirationDays` property and parse it. Final shape:

```csharp
public class S3InboxSection
{
    public bool Enabled { get; init; }
    public string BucketName { get; init; } = "";
    public string RootPath { get; init; } = "";
    public int ExpirationDays { get; init; }

    public S3InboxSection()
    {
        // Mockable support
    }

    public S3InboxSection(IConfiguration config)
    {
        Enabled = config.GetOrDefault("S3Inbox:Enabled", false);
        if (Enabled)
        {
            if (!config.GetOrDefault("S3Storage:Enabled", false))
            {
                throw new OdinConfigException("S3Storage must be enabled if S3Inbox is enabled");
            }
            BucketName = config.Required<string>("S3Inbox:BucketName");
            RootPath = config.GetOrDefault("S3Inbox:RootPath", "inbox");
            ExpirationDays = config.GetOrDefault("S3Inbox:ExpirationDays", 0);
        }
    }
}
```

- [ ] **Step 2: Add the appsettings default**

In `appsettings.json`, under the existing `"S3Inbox"` object, add `"ExpirationDays": 0`. Example:
```jsonc
"S3Inbox": {
  "Enabled": false,
  "BucketName": "your-s3-inbox-bucket-name-here",
  "ExpirationDays": 0
}
```

- [ ] **Step 3: Build to verify**

```bash
dotnet build src/services/Odin.Services/Odin.Services.csproj
```
Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/services/Odin.Services/Configuration/OdinConfiguration.cs \
        src/apps/Odin.Hosting/appsettings.json
git commit -m "Add S3Inbox:ExpirationDays config (default 0 = no expiration)"
```

---

## Task 4: `TenantPathManager` S3 inbox path anchoring

When S3 inbox is enabled, inbox paths must be bucket-root-anchored (`<tenant>/drives/...`), mirroring payloads; the `"inbox"` root folder is supplied by `S3AwsInboxStorage.rootPath`, so it must NOT appear in the path here.

**Files:**
- Modify: `src/services/Odin.Services/Drives/FileSystem/Base/TenantPathManager.cs:70-100`
- Test: `tests/services/Odin.Services.Tests/Drives/FileSystem/Base/TenantPathManagerTest.cs`

- [ ] **Step 1: Write the failing test**

Add to `TenantPathManagerTest.cs` (follow the file's existing construction style for `OdinConfiguration` + `TenantPathManager`):

```csharp
[Test]
public void TenantPathManager_S3InboxEnabled_AnchorsInboxToBucketRoot()
{
    var tenantId = Guid.NewGuid();
    var config = new OdinConfiguration
    {
        Host = new OdinConfiguration.HostSection { TenantDataRootPath = "/data/tenants" },
        S3Storage = new OdinConfiguration.S3StorageSection { Enabled = true },
        S3Inbox = new OdinConfiguration.S3InboxSection { Enabled = true },
    };

    var pm = new TenantPathManager(config, tenantId);
    var driveId = Guid.NewGuid();
    var fileId = Guid.NewGuid();

    var path = pm.GetDriveInboxFilePath(driveId, fileId, "metadata");

    // Bucket-root anchored: <tenant>/drives/<driveId>/<fileId:N>.metadata
    // No "registrations", no leading "/data", no "inbox" folder (rootPath supplies that).
    var expected = $"{tenantId}/{TenantPathManager.DrivesFolder}/{driveId:N}/{fileId:N}.metadata";
    Assert.That(path.Replace('\\', '/'), Is.EqualTo(expected));
}

[Test]
public void TenantPathManager_S3InboxDisabled_UsesOnDiskInboxPath()
{
    var tenantId = Guid.NewGuid();
    var config = new OdinConfiguration
    {
        Host = new OdinConfiguration.HostSection { TenantDataRootPath = "/data/tenants" },
    };

    var pm = new TenantPathManager(config, tenantId);
    var driveId = Guid.NewGuid();
    var fileId = Guid.NewGuid();

    var path = pm.GetDriveInboxFilePath(driveId, fileId, "metadata").Replace('\\', '/');

    Assert.That(path, Does.Contain($"registrations/{tenantId}/inbox/drives/{driveId:N}"));
    Assert.That(path, Does.EndWith($"{fileId:N}.metadata"));
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test tests/services/Odin.Services.Tests/Odin.Services.Tests.csproj \
  --filter "FullyQualifiedName~TenantPathManager_S3Inbox"
```
Expected: FAIL — `S3InboxEnabled` not handled; S3-enabled path still nests under `registrations/.../inbox`.

- [ ] **Step 3: Implement the anchoring**

In `TenantPathManager.cs`, add a public field beside `S3PayloadsEnabled` (line 70):
```csharp
public bool S3InboxEnabled { get; }
```

In the constructor, set it next to `S3PayloadsEnabled` (line 76):
```csharp
S3InboxEnabled = config.S3Inbox.Enabled;
```

Replace the current unconditional inbox assignments (lines 94-95):
```csharp
InboxPath = Path.Combine(RegistrationPath, InboxFolder);
InboxDrivesPath = Path.Combine(InboxPath, DrivesFolder);
```
with:
```csharp
if (S3InboxEnabled)
{
    // Inbox on S3 is anchored to the root of the bucket, mirroring payloads:
    // <tenant>/drives/...  The "inbox" root folder is supplied by
    // S3AwsInboxStorage's rootPath, so it must not appear here.
    InboxPath = tenant;
    InboxDrivesPath = Path.Combine(tenant, DrivesFolder);
}
else
{
    InboxPath = Path.Combine(RegistrationPath, InboxFolder);
    InboxDrivesPath = Path.Combine(InboxPath, DrivesFolder);
}
```
`RegistrationPath` is assigned at line 92, before this block — keep that ordering (the block must come after `RegistrationPath` is set; it currently does). `tenant` is the local `tenantId.ToString()` from line 77.

- [ ] **Step 4: Run test to verify it passes**

Run the Step 2 command. Expected: PASS for both tests.

- [ ] **Step 5: Commit**

```bash
git add src/services/Odin.Services/Drives/FileSystem/Base/TenantPathManager.cs \
        tests/services/Odin.Services.Tests/Drives/FileSystem/Base/TenantPathManagerTest.cs
git commit -m "TenantPathManager: anchor inbox paths to bucket root when S3 inbox enabled"
```

---

## Task 5: `IInboxReaderWriter` interface + exception

Pure interface + exception type. No tests (no behavior yet).

**Files:**
- Create: `src/services/Odin.Services/Drives/DriveCore/Storage/IInboxReaderWriter.cs`

- [ ] **Step 1: Create the interface and exception**

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Exceptions;

namespace Odin.Services.Drives.DriveCore.Storage;

#nullable enable

public interface IInboxReaderWriter
{
    Task<uint> WriteStreamAsync(string path, Stream stream, CancellationToken cancellationToken = default);
    Task<byte[]> GetAllFileBytesAsync(string path, CancellationToken cancellationToken = default);
    Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default);
    Task CreateDirectoryAsync(string dir, CancellationToken cancellationToken = default);

    // Remove every staging object/file for one fileId within the given drive inbox directory
    // (the "{fileId:N}.*" set). Disk implementation globs; S3 implementation deletes by prefix.
    Task CleanupFileSetAsync(string driveInboxDir, Guid fileId, CancellationToken cancellationToken = default);
}

//

public class InboxReaderWriterException : OdinSystemException
{
    public InboxReaderWriterException(string message) : base(message)
    {
    }

    public InboxReaderWriterException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build src/services/Odin.Services/Odin.Services.csproj
```
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/services/Odin.Services/Drives/DriveCore/Storage/IInboxReaderWriter.cs
git commit -m "Add IInboxReaderWriter abstraction"
```

---

## Task 6: `InboxFileReaderWriter` (disk implementation)

Wraps the existing `FileReaderWriter`. `CleanupFileSetAsync` carries over today's glob semantics (and the "glob is authoritative for orphan cleanup" rationale).

**Files:**
- Create: `src/services/Odin.Services/Drives/DriveCore/Storage/InboxFileReaderWriter.cs`
- Test: `tests/services/Odin.Services.Tests/Drives/DriveCore/Storage/InboxFileReaderWriterTests.cs`

- [ ] **Step 1: Write the failing test**

Create `InboxFileReaderWriterTests.cs`:

```csharp
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Services.Configuration;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Tests.Drives.DriveCore.Storage;

public class InboxFileReaderWriterTests : PayloadReaderWriterBaseTestFixture
{
    private FileReaderWriter _fileReaderWriter = null!;
    private InboxFileReaderWriter _sut = null!;

    [SetUp]
    public void Setup()
    {
        BaseSetup();
        var config = new OdinConfiguration
        {
            Host = new OdinConfiguration.HostSection
            {
                TenantDataRootPath = Path.Combine(TestRootPath, "tenants"),
                FileOperationRetryAttempts = 1,
                FileOperationRetryDelayMs = TimeSpan.FromMilliseconds(1),
            }
        };
        _fileReaderWriter = new FileReaderWriter(config, new Mock<ILogger<FileReaderWriter>>().Object);
        _sut = new InboxFileReaderWriter(_fileReaderWriter, new Mock<ILogger<InboxFileReaderWriter>>().Object);
    }

    [TearDown]
    public void TearDown() => BaseTearDown();

    [Test]
    public async Task WriteStream_ThenGetBytes_RoundTrips()
    {
        var dir = Path.Combine(TestRootPath, "drive");
        await _sut.CreateDirectoryAsync(dir);
        var path = Path.Combine(dir, "file.metadata");
        var bytes = Encoding.UTF8.GetBytes("hello inbox");

        using var stream = new MemoryStream(bytes);
        var written = await _sut.WriteStreamAsync(path, stream);

        Assert.That(written, Is.EqualTo(bytes.Length));
        Assert.That(await _sut.FileExistsAsync(path), Is.True);
        Assert.That(await _sut.GetAllFileBytesAsync(path), Is.EqualTo(bytes));
    }

    [Test]
    public async Task CleanupFileSet_DeletesOnlyMatchingFileId()
    {
        var dir = Path.Combine(TestRootPath, "drive");
        Directory.CreateDirectory(dir);
        var fileId = Guid.NewGuid();
        var otherId = Guid.NewGuid();

        var target1 = Path.Combine(dir, $"{fileId:N}.metadata");
        var target2 = Path.Combine(dir, $"{fileId:N}.foo-1.payload");
        var survivor = Path.Combine(dir, $"{otherId:N}.foo-2.payload");
        File.WriteAllText(target1, "x");
        File.WriteAllText(target2, "x");
        File.WriteAllText(survivor, "x");

        await _sut.CleanupFileSetAsync(dir, fileId);

        Assert.That(File.Exists(target1), Is.False);
        Assert.That(File.Exists(target2), Is.False);
        Assert.That(File.Exists(survivor), Is.True);
    }

    [Test]
    public async Task CleanupFileSet_NoThrow_WhenDirMissing()
    {
        var dir = Path.Combine(TestRootPath, "does-not-exist");
        await _sut.CleanupFileSetAsync(dir, Guid.NewGuid()); // must not throw
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test tests/services/Odin.Services.Tests/Odin.Services.Tests.csproj \
  --filter "FullyQualifiedName~InboxFileReaderWriterTests"
```
Expected: FAIL to compile — `InboxFileReaderWriter` not defined.

- [ ] **Step 3: Implement `InboxFileReaderWriter`**

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Services.Drives.FileSystem.Base;

namespace Odin.Services.Drives.DriveCore.Storage;

#nullable enable

public class InboxFileReaderWriter(
    FileReaderWriter fileReaderWriter,
    ILogger<InboxFileReaderWriter> logger) : IInboxReaderWriter
{
    public async Task<uint> WriteStreamAsync(string path, Stream stream, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await fileReaderWriter.WriteStreamAsync(path, stream);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new InboxReaderWriterException(e.Message, e);
        }
    }

    public async Task<byte[]> GetAllFileBytesAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await fileReaderWriter.GetAllFileBytesAsync(path);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new InboxReaderWriterException(e.Message, e);
        }
    }

    public Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(fileReaderWriter.FileExists(path));
    }

    public Task CreateDirectoryAsync(string dir, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        fileReaderWriter.CreateDirectory(dir);
        return Task.CompletedTask;
    }

    // The drive inbox dir is a single-purpose staging area where every file is prefixed with
    // "{fileId:N}." A glob on that prefix is the authoritative way to remove everything for a
    // given fileId, independent of whatever descriptors an in-flight processor managed to parse.
    public Task CleanupFileSetAsync(string driveInboxDir, Guid fileId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(driveInboxDir))
        {
            return Task.CompletedTask;
        }
        var prefix = TenantPathManager.GuidToPathSafeString(fileId);
        var matches = Directory.GetFiles(driveInboxDir, prefix + ".*");
        fileReaderWriter.DeleteFiles(matches);
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run the Step 2 command. Expected: PASS (all three tests).

- [ ] **Step 5: Commit**

```bash
git add src/services/Odin.Services/Drives/DriveCore/Storage/InboxFileReaderWriter.cs \
        tests/services/Odin.Services.Tests/Drives/DriveCore/Storage/InboxFileReaderWriterTests.cs
git commit -m "Add InboxFileReaderWriter (disk inbox storage)"
```

---

## Task 7: `InboxS3ReaderWriter` (S3 implementation)

Wraps `IS3InboxStorage` with the same retry policy as `PayloadS3ReaderWriter`.

**Files:**
- Create: `src/services/Odin.Services/Drives/DriveCore/Storage/InboxS3ReaderWriter.cs`
- Test: `tests/services/Odin.Services.Tests/Drives/DriveCore/Storage/InboxS3ReaderWriterTests.cs`

- [ ] **Step 1: Write the failing test**

Create `InboxS3ReaderWriterTests.cs`, modeled on `PayloadS3ReaderWriterTests.cs` (copy its MinIO/Hetzner SetUp/TearDown/DeleteAllObjectsAsync verbatim, changing the storage type to inbox):

```csharp
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Storage.ObjectStorage;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Test.Helpers.Logging;
using Odin.Test.Helpers.Secrets;
using Testcontainers.Minio;

namespace Odin.Services.Tests.Drives.DriveCore.Storage;

#if RUN_S3_TESTS

public class InboxS3ReaderWriterTests : PayloadReaderWriterBaseTestFixture
{
    private string _bucketName = "";
    private IAmazonS3 _s3Client = null!;
    private IS3InboxStorage _s3InboxStorage = null!;
    private MinioContainer _minioContainer = null!;

    [SetUp]
    public async Task Setup()
    {
        BaseSetup();
        TestSecrets.Load();

        // (Copy the MinIO/Hetzner client setup block from PayloadS3ReaderWriterTests.Setup verbatim.)
        _minioContainer = new MinioBuilder()
            .WithImage("minio/minio:RELEASE.2025-05-24T17-08-30Z")
            .WithUsername("minioadmin")
            .WithPassword("minioadmin123")
            .Build();
        await _minioContainer.StartAsync();

        _s3Client = new AmazonS3Client(
            _minioContainer.GetAccessKey(),
            _minioContainer.GetSecretKey(),
            new AmazonS3Config
            {
                ServiceURL = _minioContainer.GetConnectionString(),
                AuthenticationRegion = "foo",
                ForcePathStyle = true,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED
            });

        _bucketName = $"zz-ci-test-{Guid.NewGuid():N}";
        await _s3Client.PutBucketAsync(_bucketName);

        var logger = TestLogFactory.CreateConsoleLogger<S3AwsInboxStorage>();
        _s3InboxStorage = new S3AwsInboxStorage(logger, _s3Client, _bucketName, "inbox");
    }

    [TearDown]
    public async Task TearDown()
    {
        try
        {
            await DeleteAllObjectsAsync(_bucketName);
            await _s3Client.DeleteBucketAsync(new DeleteBucketRequest { BucketName = _bucketName });
        }
        finally
        {
            BaseTearDown();
            if (_minioContainer != null) await _minioContainer.DisposeAsync();
        }
    }

    private async Task DeleteAllObjectsAsync(string bucketName)
    {
        string? continuationToken = null;
        do
        {
            var listResponse = await _s3Client.ListObjectsV2Async(new ListObjectsV2Request
            { BucketName = bucketName, ContinuationToken = continuationToken, MaxKeys = 1000 });
            if (listResponse.S3Objects is { Count: > 0 })
            {
                await _s3Client.DeleteObjectsAsync(new DeleteObjectsRequest
                {
                    BucketName = bucketName,
                    Objects = listResponse.S3Objects.Select(o => new KeyVersion { Key = o.Key }).ToList()
                });
            }
            continuationToken = listResponse.NextContinuationToken;
        }
        while (continuationToken != null);
    }

    private InboxS3ReaderWriter CreateSut() =>
        new(TestLogFactory.CreateConsoleLogger<InboxS3ReaderWriter>(), _s3InboxStorage);

    [Test]
    public async Task WriteStream_ThenGetBytes_RoundTrips()
    {
        var sut = CreateSut();
        var path = $"{Guid.NewGuid()}/drives/{Guid.NewGuid():N}/{Guid.NewGuid():N}.metadata";
        var bytes = "hello inbox".ToUtf8ByteArray();

        using var stream = new MemoryStream(bytes);
        var written = await sut.WriteStreamAsync(path, stream);
        await Task.Delay(100);

        Assert.That(written, Is.EqualTo(bytes.Length));
        Assert.That(await sut.FileExistsAsync(path), Is.True);
        Assert.That(await sut.GetAllFileBytesAsync(path), Is.EqualTo(bytes));
    }

    [Test]
    public async Task CleanupFileSet_DeletesOnlyMatchingFileId()
    {
        var sut = CreateSut();
        var driveDir = $"{Guid.NewGuid()}/drives/{Guid.NewGuid():N}";
        var fileId = Guid.NewGuid();
        var otherId = Guid.NewGuid();

        async Task Write(string key)
        {
            using var s = new MemoryStream("x".ToUtf8ByteArray());
            await sut.WriteStreamAsync(key, s);
        }

        var t1 = $"{driveDir}/{fileId:N}.metadata";
        var t2 = $"{driveDir}/{fileId:N}.foo-1.payload";
        var survivor = $"{driveDir}/{otherId:N}.foo-2.payload";
        await Write(t1); await Write(t2); await Write(survivor);
        await Task.Delay(100);

        await sut.CleanupFileSetAsync(driveDir, fileId);
        await Task.Delay(100);

        Assert.That(await sut.FileExistsAsync(t1), Is.False);
        Assert.That(await sut.FileExistsAsync(t2), Is.False);
        Assert.That(await sut.FileExistsAsync(survivor), Is.True);
    }
}

#endif
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test tests/services/Odin.Services.Tests/Odin.Services.Tests.csproj \
  --filter "FullyQualifiedName~InboxS3ReaderWriterTests"
```
Expected: FAIL to compile — `InboxS3ReaderWriter` not defined. (Docker + RUN_S3_TESTS.)

- [ ] **Step 3: Implement `InboxS3ReaderWriter`**

Mirror `PayloadS3ReaderWriter`'s retry policy exactly (5 attempts, exponential backoff 5s, retry 5xx/timeout not 4xx).

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Microsoft.Extensions.Logging;
using Odin.Core.Storage.ObjectStorage;
using Odin.Core.Util;

namespace Odin.Services.Drives.DriveCore.Storage;

#nullable enable

public class InboxS3ReaderWriter(ILogger<InboxS3ReaderWriter> logger, IS3InboxStorage s3InboxStorage)
    : IInboxReaderWriter
{
    public async Task<uint> WriteStreamAsync(string path, Stream stream, CancellationToken cancellationToken = default)
    {
        try
        {
            var written = await TryRetry(async () =>
                await s3InboxStorage.WriteStreamAsync(path, stream, cancellationToken), cancellationToken);
            return (uint)written;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new InboxReaderWriterException(e.Message, e);
        }
    }

    public async Task<byte[]> GetAllFileBytesAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            return await TryRetry(async () =>
                await s3InboxStorage.ReadBytesAsync(path, cancellationToken), cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new InboxReaderWriterException(e.Message, e);
        }
    }

    public async Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            return await TryRetry(async () =>
                await s3InboxStorage.FileExistsAsync(path, cancellationToken), cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new InboxReaderWriterException(e.Message, e);
        }
    }

    public Task CreateDirectoryAsync(string dir, CancellationToken cancellationToken = default)
    {
        // No-op: S3 does not have directories in the same way as a file system.
        return Task.CompletedTask;
    }

    public async Task CleanupFileSetAsync(string driveInboxDir, Guid fileId, CancellationToken cancellationToken = default)
    {
        var prefix = S3Path.Combine(driveInboxDir, $"{fileId:N}.");
        try
        {
            await TryRetry(async () =>
                await s3InboxStorage.DeleteByPrefixAsync(prefix, cancellationToken), cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new InboxReaderWriterException(e.Message, e);
        }
    }

    private RetryBuilder CreateRetry(CancellationToken cancellationToken)
    {
        return TryRetry.Create()
            .WithAttempts(5)
            .WithExponentialBackoff(TimeSpan.FromSeconds(5))
            .WithCancellation(cancellationToken)
            .WithLogging(logger)
            .WithoutExceptionWrapper()
            .RetryOnPredicate((ex, _) =>
            {
                if (ex.InnerException is not AmazonS3Exception s3Ex)
                {
                    return false;
                }
                if (s3Ex.Message.Contains("did not respond in time"))
                {
                    return true;
                }
                if ((int)s3Ex.StatusCode >= 500)
                {
                    return true;
                }
                if (s3Ex.InnerException is Amazon.Runtime.Internal.HttpErrorResponseException httpException)
                {
                    if ((int)httpException.Response.StatusCode >= 500)
                    {
                        return true;
                    }
                }
                return false; // do not retry 4xx
            });
    }

    private Task<T> TryRetry<T>(Func<Task<T>> operation, CancellationToken ct)
        => CreateRetry(ct).ExecuteAsync(operation);

    private Task TryRetry(Func<Task> operation, CancellationToken ct)
        => CreateRetry(ct).ExecuteAsync(operation);
}
```

Note: `TryRetry`, `RetryBuilder` are in `Odin.Core.Util` (already imported). This `CreateRetry` method is copied verbatim from `PayloadS3ReaderWriter.cs:147-186` — keep it identical so both readers/writers share one retry policy. If a future refactor extracts that policy into a shared helper, do it for both at once (out of scope here).

- [ ] **Step 4: Run test to verify it passes**

Run the Step 2 command. Expected: PASS (Docker + RUN_S3_TESTS).

- [ ] **Step 5: Commit**

```bash
git add src/services/Odin.Services/Drives/DriveCore/Storage/InboxS3ReaderWriter.cs \
        tests/services/Odin.Services.Tests/Drives/DriveCore/Storage/InboxS3ReaderWriterTests.cs
git commit -m "Add InboxS3ReaderWriter (S3 inbox storage)"
```

---

## Task 8: Refactor `InboxStorageManager` onto `IInboxReaderWriter`

Make the manager backend-agnostic; remove direct `FileReaderWriter`/`Directory` use. Update existing tests to inject the disk implementation.

**Files:**
- Modify: `src/services/Odin.Services/Drives/DriveCore/Storage/InboxStorageManager.cs`
- Modify: `tests/services/Odin.Services.Tests/Drives/DriveCore/Storage/InboxStorageManagerTests.cs:50-51`

- [ ] **Step 1: Update the existing test setup to drive the new constructor (this is the failing step)**

In `InboxStorageManagerTests.cs`, replace the `_sut` construction (line 51) so it injects the disk reader/writer. Change the field at line 21 region and Setup:

Replace:
```csharp
_fileReaderWriter = new FileReaderWriter(_config, new Mock<ILogger<FileReaderWriter>>().Object);
_sut = new InboxStorageManager(_fileReaderWriter, new Mock<ILogger<InboxStorageManager>>().Object, _tenantContext);
```
with:
```csharp
_fileReaderWriter = new FileReaderWriter(_config, new Mock<ILogger<FileReaderWriter>>().Object);
var inboxReaderWriter = new InboxFileReaderWriter(
    _fileReaderWriter, new Mock<ILogger<InboxFileReaderWriter>>().Object);
_sut = new InboxStorageManager(inboxReaderWriter, new Mock<ILogger<InboxStorageManager>>().Object, _tenantContext);
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test tests/services/Odin.Services.Tests/Odin.Services.Tests.csproj \
  --filter "FullyQualifiedName~InboxStorageManagerTests"
```
Expected: FAIL to compile — `InboxStorageManager` constructor still takes `FileReaderWriter`.

- [ ] **Step 3: Refactor `InboxStorageManager`**

Replace the whole file with:

```csharp
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Services.Base;
using Odin.Services.Drives.FileSystem.Base;

namespace Odin.Services.Drives.DriveCore.Storage
{
    /// <summary>
    /// Temporary storage for a given drive. Used to stage incoming file parts from peer transfers (inbox).
    /// Backed by local disk or S3 depending on configuration, via <see cref="IInboxReaderWriter"/>.
    /// </summary>
    public class InboxStorageManager(
        IInboxReaderWriter inboxReaderWriter,
        ILogger<InboxStorageManager> logger,
        TenantContext tenantContext)
    {
        private readonly TenantPathManager _tenantPathManager = tenantContext.TenantPathManager;

        public Task<bool> InboxFileExists(InternalDriveFileId file, string extension)
        {
            var path = _tenantPathManager.GetDriveInboxFilePath(file.DriveId, file.FileId, extension);
            return inboxReaderWriter.FileExistsAsync(path);
        }

        /// <summary>
        /// Gets all bytes for the specified file
        /// </summary>
        public Task<byte[]> GetAllInboxFileBytes(InternalDriveFileId file, string extension)
        {
            var path = _tenantPathManager.GetDriveInboxFilePath(file.DriveId, file.FileId, extension);
            return inboxReaderWriter.GetAllFileBytesAsync(path);
        }

        /// <summary>
        /// Writes a stream for a given file and part to the configured provider.
        /// </summary>
        public async Task<uint> WriteInboxStream(InternalDriveFileId file, string extension, Stream stream)
        {
            var driveDir = _tenantPathManager.GetDriveInboxPath(file.DriveId);
            await inboxReaderWriter.CreateDirectoryAsync(driveDir);
            var path = _tenantPathManager.GetDriveInboxFilePath(file.DriveId, file.FileId, extension);
            return await inboxReaderWriter.WriteStreamAsync(path, stream);
        }

        // The drive inbox dir is a single-purpose staging area where every file is prefixed with
        // "{fileId:N}." (see TenantPathManager.GetFilename). Cleanup removes that whole set for the
        // fileId — independent of whatever descriptors an in-flight processor managed to parse —
        // which prevents the .payload/.thumb orphan leak that a descriptor-driven cleanup caused.
        public async Task CleanupInboxFiles(InternalDriveFileId file)
        {
            logger.LogDebug("CleanupInboxFiles called - file: {file}", file);
            try
            {
                var driveDir = _tenantPathManager.GetDriveInboxPath(file.DriveId);
                await inboxReaderWriter.CleanupFileSetAsync(driveDir, file.FileId);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failure while cleaning up inbox files for {file}", file);
            }
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run the Step 2 command. Expected: PASS (the three existing cleanup tests plus anything else in the class).

- [ ] **Step 5: Commit**

```bash
git add src/services/Odin.Services/Drives/DriveCore/Storage/InboxStorageManager.cs \
        tests/services/Odin.Services.Tests/Drives/DriveCore/Storage/InboxStorageManagerTests.cs
git commit -m "InboxStorageManager: depend on IInboxReaderWriter (disk/S3 agnostic)"
```

---

## Task 9: DI wiring

Select the inbox reader/writer implementation by `S3Inbox.Enabled`, beside the payload registration.

**Files:**
- Modify: `src/apps/Odin.Hosting/TenantServices.cs:366-374`

- [ ] **Step 1: Add the registration**

Immediately after the payload storage block (line 374, before `return cb;`), add:

```csharp
// Inbox storage
if (odinConfig.S3Inbox.Enabled)
{
    cb.RegisterType<InboxS3ReaderWriter>().As<IInboxReaderWriter>().SingleInstance();
}
else
{
    cb.RegisterType<InboxFileReaderWriter>().As<IInboxReaderWriter>().SingleInstance();
}
```

Confirm the using/namespace for `InboxS3ReaderWriter` / `InboxFileReaderWriter` resolves (they live in `Odin.Services.Drives.DriveCore.Storage`, already referenced for the payload types).

- [ ] **Step 2: Build to verify**

```bash
dotnet build src/apps/Odin.Hosting/Odin.Hosting.csproj
```
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/apps/Odin.Hosting/TenantServices.cs
git commit -m "DI: register IInboxReaderWriter (S3 when S3Inbox enabled, else disk)"
```

---

## Task 10: Startup lifecycle reconciliation

Apply/remove the inbox expiration rule at startup, next to the existing inbox bucket-ensure.

**Files:**
- Modify: `src/apps/Odin.Hosting/Startup.cs:456-464`

- [ ] **Step 1: Add the reconciliation call**

Extend the existing S3 inbox block (lines 458-464) so it reconciles the lifecycle rule after ensuring the bucket:

```csharp
// Ensure S3 inbox bucket exists
logger.LogInformation("S3Inbox enabled: {enabled}", config.S3Inbox.Enabled);
if (config.S3Inbox.Enabled)
{
    logger.LogInformation("Creating S3 inbox bucket '{BucketName}' at {ServiceUrl}",
        config.S3Inbox.BucketName, config.S3Storage.ServiceUrl);
    var inboxBucket = services.GetRequiredService<IS3InboxStorage>();
    inboxBucket.CreateBucketAsync().BlockingWait();

    logger.LogInformation("Reconciling S3 inbox expiration: {days} day(s)", config.S3Inbox.ExpirationDays);
    inboxBucket.EnsureExpirationLifecycleAsync(config.S3Inbox.ExpirationDays).BlockingWait();
}
```

(Also rename the misleading local `payloadBucket` to `inboxBucket` as shown.)

- [ ] **Step 2: Build to verify**

```bash
dotnet build src/apps/Odin.Hosting/Odin.Hosting.csproj
```
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/apps/Odin.Hosting/Startup.cs
git commit -m "Startup: reconcile S3 inbox expiration lifecycle (configurable, default off)"
```

---

## Task 11: Full build + regression sweep

- [ ] **Step 1: Build the solution**

```bash
dotnet build ./odin-core.sln
```
Expected: build succeeds.

- [ ] **Step 2: Run the non-S3 affected tests**

```bash
dotnet test tests/services/Odin.Services.Tests/Odin.Services.Tests.csproj \
  --filter "FullyQualifiedName~InboxStorageManagerTests|FullyQualifiedName~InboxFileReaderWriterTests|FullyQualifiedName~TenantPathManager"
```
Expected: PASS.

- [ ] **Step 3: (CI / Docker) Run the S3 suites**

```bash
# Requires Docker and the RUN_S3_TESTS constant
dotnet test tests/core/Odin.Core.Storage.Tests/Odin.Core.Storage.Tests.csproj \
  --filter "FullyQualifiedName~S3AwsStorage"
dotnet test tests/services/Odin.Services.Tests/Odin.Services.Tests.csproj \
  --filter "FullyQualifiedName~InboxS3ReaderWriterTests"
```
Expected: PASS.

- [ ] **Step 4: Sanity-check no other callers broke**

Confirm nothing else constructed `InboxStorageManager` with a `FileReaderWriter` or referenced the removed `InboxStorageManager`/glob internals:
```bash
grep -rn "new InboxStorageManager(" src/ tests/ | grep -v "/bin/"
```
Expected: only the DI registration (which uses Autofac, not `new`) and the updated test. If any other `new InboxStorageManager(... FileReaderWriter ...)` remains, fix it to pass an `IInboxReaderWriter`.

---

## Self-review notes (already reconciled against the spec)

- Spec §1 `IInboxReaderWriter` → Tasks 5-7. Stream-based write preserved (Task 5/6/7 `WriteStreamAsync`).
- Spec §2 `DeleteByPrefixAsync` on base class → Task 1.
- Spec §3 `InboxStorageManager` refactor → Task 8.
- Spec §4 `TenantPathManager` anchoring → Task 4 (identical flat `<fileId:N>.<ext>` filename on both backends, no doubled `inbox` folder).
- Spec §5 DI wiring → Task 9.
- Spec §6 configurable expiration (default off) → Tasks 2, 3, 10.
- Out-of-scope items (direct-write path, PeerFileWriter, inbox DB queue) are untouched.
