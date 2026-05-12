using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Services.Background.BackgroundServices.Tenant;

namespace Odin.Services.Tests.BackgroundServices.Services.Tenant;

[TestFixture]
public class InboxOrphanScanTests
{
    private string _inboxDrivesRoot = "";
    private Guid _drive1Id;
    private string _drive1Path = "";
    private Mock<ILogger> _loggerMock = new();
    private TimeSpan _ageThreshold;

    // Stub for the "pending fileIds per drive" predicate. Tests populate this; the
    // helper PendingForDriveAsync reads it.
    private readonly Dictionary<Guid, HashSet<Guid>> _pendingByDrive = new();

    [SetUp]
    public void Setup()
    {
        // Layout under test:
        //   <inboxDrivesRoot>/<driveId:N>/<fileId:N>.<ext>
        // mirroring TenantPathManager.InboxDrivesPath / GetDriveInboxFilePath.
        _inboxDrivesRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "drives");
        _drive1Id = Guid.NewGuid();
        _drive1Path = Path.Combine(_inboxDrivesRoot, _drive1Id.ToString("N"));
        Directory.CreateDirectory(_drive1Path);

        _loggerMock = new Mock<ILogger>();
        _ageThreshold = TimeSpan.FromHours(2);
        _pendingByDrive.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        var root = Path.GetDirectoryName(_inboxDrivesRoot)!;
        if (!Directory.Exists(root))
        {
            return;
        }

        try
        {
            Directory.Delete(root, true);
        }
        catch (IOException)
        {
            Thread.Sleep(100);
            try { Directory.Delete(root, true); }
            catch { Console.WriteLine($"Warning: Could not delete temp directory {root}"); }
        }
    }

    [Test]
    public async Task ExecuteAsync_FlagsOrphan_WhenFileOldAndFileIdNotInPendingSet()
    {
        // Arrange — file written 5h ago, drive's pending set is empty → orphan.
        var orphanFileId = Guid.NewGuid();
        var orphan = InboxFilePath(_drive1Path, orphanFileId, "metadata");
        CreateFileWithTimestamp(orphan, DateTime.UtcNow.Subtract(TimeSpan.FromHours(5)));

        // Act
        await InboxOrphanScan.ExecuteAsync(
            _loggerMock.Object, _inboxDrivesRoot, _ageThreshold, PendingForDriveAsync);

        // Assert
        VerifyLog(LogLevel.Debug, "Inbox orphan:", Times.Once());
        VerifyLog(LogLevel.Error, "Inbox orphan summary:", Times.Once());
        Assert.That(File.Exists(orphan), Is.True, "Detection-only — file must NOT be deleted");
    }

    [Test]
    public async Task ExecuteAsync_DoesNotFlag_WhenFileIdIsInPendingSet()
    {
        // Arrange — old file, but its fileId is in the pending set for this drive.
        var fileId = Guid.NewGuid();
        var file = InboxFilePath(_drive1Path, fileId, "metadata");
        CreateFileWithTimestamp(file, DateTime.UtcNow.Subtract(TimeSpan.FromHours(48)));

        SetPending(_drive1Id, fileId);

        // Act
        await InboxOrphanScan.ExecuteAsync(
            _loggerMock.Object, _inboxDrivesRoot, _ageThreshold, PendingForDriveAsync);

        // Assert
        VerifyLog(LogLevel.Error, "Inbox orphan", Times.Never());
    }

    [Test]
    public async Task ExecuteAsync_FlagsLeakedFile_OnBusyDrive()
    {
        // The whole reason for the per-fileId set lookup: a drive that has legitimately-pending
        // items must not hide leaks. Here we have one pending fileId and one leaked fileId in
        // the same drive folder. The leaked one must be flagged; the pending one must not.
        var pendingFileId = Guid.NewGuid();
        var leakedFileId = Guid.NewGuid();

        var pendingFile = InboxFilePath(_drive1Path, pendingFileId, "metadata");
        var leakedFile  = InboxFilePath(_drive1Path, leakedFileId,  "metadata");

        var older = DateTime.UtcNow.Subtract(TimeSpan.FromHours(5));
        CreateFileWithTimestamp(pendingFile, older);
        CreateFileWithTimestamp(leakedFile, older);

        // Inbox table only knows about the pending one.
        SetPending(_drive1Id, pendingFileId);

        await InboxOrphanScan.ExecuteAsync(
            _loggerMock.Object, _inboxDrivesRoot, _ageThreshold, PendingForDriveAsync);

        VerifyLog(LogLevel.Debug, "Inbox orphan:", Times.Once());
        VerifyLog(LogLevel.Error, "Inbox orphan summary:", Times.Once());
    }

    [Test]
    public async Task ExecuteAsync_DoesNotFlag_WhenFileIsRecent()
    {
        // Arrange — file written 30min ago, threshold 2h. Pending set is empty, but the file
        // is under the age gate so it must not be flagged (could be mid-upload).
        var file = InboxFilePath(_drive1Path, Guid.NewGuid(), "metadata");
        CreateFileWithTimestamp(file, DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(30)));

        await InboxOrphanScan.ExecuteAsync(
            _loggerMock.Object, _inboxDrivesRoot, _ageThreshold, PendingForDriveAsync);

        VerifyLog(LogLevel.Error, "Inbox orphan", Times.Never());
    }

    [Test]
    public void ExecuteAsync_RespectsStoppingToken()
    {
        // Arrange — orphan-eligible file, but token already cancelled.
        var orphan = InboxFilePath(_drive1Path, Guid.NewGuid(), "metadata");
        CreateFileWithTimestamp(orphan, DateTime.UtcNow.Subtract(TimeSpan.FromHours(5)));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(() =>
            InboxOrphanScan.ExecuteAsync(
                _loggerMock.Object, _inboxDrivesRoot, _ageThreshold, PendingForDriveAsync, cts.Token));
    }

    [Test]
    public void ExecuteAsync_ReturnsSilently_WhenInboxDrivesPathDoesNotExist()
    {
        var nonExistent = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var queried = 0;
        Func<Guid, Task<HashSet<Guid>>> probe = _ =>
        {
            queried++;
            return Task.FromResult(new HashSet<Guid>());
        };

        Assert.DoesNotThrowAsync(() =>
            InboxOrphanScan.ExecuteAsync(
                _loggerMock.Object, nonExistent, _ageThreshold, probe));

        // The probe should not even be called when the path doesn't exist.
        Assert.That(queried, Is.EqualTo(0));
    }

    [Test]
    public async Task ExecuteAsync_OneInboxLookupPerDriveFolder()
    {
        // A single inbox transfer typically stages multiple files sharing the same FileId
        // (.metadata + .transferkeyheader + .payload + thumbnails). Whatever the file count,
        // the scanner does exactly one inbox lookup per drive folder.
        var older = DateTime.UtcNow.Subtract(TimeSpan.FromHours(5));
        var fileId = Guid.NewGuid();

        CreateFileWithTimestamp(InboxFilePath(_drive1Path, fileId, "metadata"), older);
        CreateFileWithTimestamp(InboxFilePath(_drive1Path, fileId, "transferkeyheader"), older);
        CreateFileWithTimestamp(InboxFilePath(_drive1Path, fileId, "convo_img-12345.payload"), older);
        CreateFileWithTimestamp(InboxFilePath(_drive1Path, fileId, "convo_img-12345-320x240.thumb"), older);

        var queries = new List<Guid>();
        Func<Guid, Task<HashSet<Guid>>> probe = id =>
        {
            queries.Add(id);
            return Task.FromResult(new HashSet<Guid>());
        };

        await InboxOrphanScan.ExecuteAsync(
            _loggerMock.Object, _inboxDrivesRoot, _ageThreshold, probe);

        Assert.That(queries, Has.Count.EqualTo(1));
        Assert.That(queries[0], Is.EqualTo(_drive1Id));

        // All four files get flagged.
        VerifyLog(LogLevel.Debug, "Inbox orphan:", Times.Exactly(4));
        VerifyLog(LogLevel.Error, "Inbox orphan summary:", Times.Once());
    }

    [Test]
    public async Task ExecuteAsync_SkipsFoldersWithUnparseableNames()
    {
        // Stray subdirectory under inbox/drives that isn't a {driveId:N} name — we have no way
        // to map it to a drive row, so we leave it (and its contents) alone.
        var stray = Path.Combine(_inboxDrivesRoot, "not-a-guid");
        Directory.CreateDirectory(stray);
        CreateFileWithTimestamp(
            Path.Combine(stray, $"{Guid.NewGuid():N}.metadata"),
            DateTime.UtcNow.Subtract(TimeSpan.FromDays(7)));

        var queried = new List<Guid>();
        Func<Guid, Task<HashSet<Guid>>> probe = id =>
        {
            queried.Add(id);
            return Task.FromResult(new HashSet<Guid>());
        };

        await InboxOrphanScan.ExecuteAsync(
            _loggerMock.Object, _inboxDrivesRoot, _ageThreshold, probe);

        // Only the legit drive folder was queried. The stray was skipped.
        Assert.That(queried, Is.EquivalentTo(new[] { _drive1Id }));
        VerifyLog(LogLevel.Error, "Inbox orphan", Times.Never());
    }

    [Test]
    public async Task ExecuteAsync_SkipsFilesWithUnparseableNames()
    {
        // README.txt or similar — not a {fileId:N}.{ext} name. Should be left alone.
        var stray = Path.Combine(_drive1Path, "README.txt");
        CreateFileWithTimestamp(stray, DateTime.UtcNow.Subtract(TimeSpan.FromDays(7)));

        await InboxOrphanScan.ExecuteAsync(
            _loggerMock.Object, _inboxDrivesRoot, _ageThreshold, PendingForDriveAsync);

        VerifyLog(LogLevel.Error, "Inbox orphan", Times.Never());
    }

    [Test]
    public async Task ExecuteAsync_DoesNotFlag_WhenLookupThrows()
    {
        // Conservative: a flaky lookup must not produce false positives.
        var file = InboxFilePath(_drive1Path, Guid.NewGuid(), "metadata");
        CreateFileWithTimestamp(file, DateTime.UtcNow.Subtract(TimeSpan.FromHours(5)));

        Func<Guid, Task<HashSet<Guid>>> probe = _ => throw new InvalidOperationException("db down");

        await InboxOrphanScan.ExecuteAsync(
            _loggerMock.Object, _inboxDrivesRoot, _ageThreshold, probe);

        // No per-file flag and no summary — the failed lookup short-circuited the drive scan.
        VerifyLog(LogLevel.Debug, "Inbox orphan:", Times.Never());
        VerifyLog(LogLevel.Error, "Inbox orphan summary:", Times.Never());
    }

    //

    private Task<HashSet<Guid>> PendingForDriveAsync(Guid driveId)
    {
        var set = _pendingByDrive.TryGetValue(driveId, out var s) ? s : new HashSet<Guid>();
        return Task.FromResult(set);
    }

    private void SetPending(Guid driveId, params Guid[] fileIds)
    {
        _pendingByDrive[driveId] = fileIds.ToHashSet();
    }

    private static string InboxFilePath(string driveFolder, Guid fileId, string extension)
    {
        return Path.Combine(driveFolder, $"{fileId:N}.{extension}");
    }

    private void VerifyLog(LogLevel level, string fragment, Times times)
    {
        _loggerMock.Verify(
            x => x.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, _) => o.ToString()!.Contains(fragment)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()!),
            times);
    }

    private static void CreateFileWithTimestamp(string filePath, DateTime utc)
    {
        File.WriteAllText(filePath, "test");
        File.SetCreationTimeUtc(filePath, utc);
        File.SetLastWriteTimeUtc(filePath, utc);
    }
}
