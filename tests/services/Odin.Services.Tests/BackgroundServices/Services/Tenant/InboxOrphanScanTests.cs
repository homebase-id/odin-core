using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Services.Background.BackgroundServices.Tenant;
using Odin.Services.LastSeen;

namespace Odin.Services.Tests.BackgroundServices.Services.Tenant;

[TestFixture]
public class InboxOrphanScanTests
{
    private string _inboxDrivesRoot = "";
    private string _drive1Path = "";
    private Mock<ILogger> _loggerMock = new();
    private Mock<ILastSeenService> _lastSeenMock = new();
    private TimeSpan _ageThreshold;
    private OdinId _hostOdinId;

    [SetUp]
    public void Setup()
    {
        // Layout under test:
        //   <inboxDrivesRoot>/<driveId>/<fileId>.<ext>
        // mirroring TenantPathManager.InboxDrivesPath / GetDriveInboxFilePath.
        _inboxDrivesRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "drives");
        _drive1Path = Path.Combine(_inboxDrivesRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_drive1Path);

        _loggerMock = new Mock<ILogger>();
        _lastSeenMock = new Mock<ILastSeenService>();
        _ageThreshold = TimeSpan.FromHours(2);
        _hostOdinId = (OdinId)"frodo.dotyou.cloud";
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
    public async Task ExecuteAsync_FlagsOrphan_WhenFileOldAndTenantSeenAfterThreshold()
    {
        // Arrange — file written 5h ago, tenant seen 30min ago (well after fileWrite + 2h).
        var fileWrite = DateTime.UtcNow.Subtract(TimeSpan.FromHours(5));
        var orphan = Path.Combine(_drive1Path, "orphan.metadata");
        CreateFileWithTimestamp(orphan, fileWrite);

        SetupLastSeen(DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(30)));

        // Act
        await InboxOrphanScan.ExecuteAsync(
            _loggerMock.Object, _inboxDrivesRoot, _ageThreshold, _lastSeenMock.Object, _hostOdinId);

        // Assert — per-file orphan log + summary log
        VerifyLog(LogLevel.Error, "Inbox orphan:", Times.Once());
        VerifyLog(LogLevel.Error, "Inbox orphan summary:", Times.Once());
        Assert.That(File.Exists(orphan), Is.True, "Detection-only — file must NOT be deleted");
    }

    [Test]
    public async Task ExecuteAsync_DoesNotFlag_WhenLastSeenIsNull()
    {
        // Arrange — old file but tenant never seen.
        var orphan = Path.Combine(_drive1Path, "old.metadata");
        CreateFileWithTimestamp(orphan, DateTime.UtcNow.Subtract(TimeSpan.FromHours(48)));

        _lastSeenMock
            .Setup(s => s.GetLastSeenAsync(It.IsAny<OdinId>()))
            .ReturnsAsync((UnixTimeUtc?)null);

        // Act
        await InboxOrphanScan.ExecuteAsync(
            _loggerMock.Object, _inboxDrivesRoot, _ageThreshold, _lastSeenMock.Object, _hostOdinId);

        // Assert — nothing logged at error level
        VerifyLog(LogLevel.Error, "Inbox orphan", Times.Never());
    }

    [Test]
    public async Task ExecuteAsync_DoesNotFlag_WhenTenantNotSeenSinceFileBecameOrphanEligible()
    {
        // Arrange — file written 90min ago. ageThreshold = 2h, so file is NOT yet old enough,
        // but even if we tighten the rules, the tenant was last seen 2 days ago which is
        // before fileWrite+threshold. Use file just past the threshold to make the check meaningful:
        var fileWrite = DateTime.UtcNow.Subtract(TimeSpan.FromHours(3)); // > threshold ago, so > cutoffTime
        var file = Path.Combine(_drive1Path, "borderline.metadata");
        CreateFileWithTimestamp(file, fileWrite);

        // Tenant last seen 2 days ago — well before fileWrite + threshold (which is ~1h ago).
        SetupLastSeen(DateTime.UtcNow.Subtract(TimeSpan.FromDays(2)));

        // Act
        await InboxOrphanScan.ExecuteAsync(
            _loggerMock.Object, _inboxDrivesRoot, _ageThreshold, _lastSeenMock.Object, _hostOdinId);

        // Assert — file is old, but tenant hasn't been around to process it
        VerifyLog(LogLevel.Error, "Inbox orphan", Times.Never());
    }

    [Test]
    public async Task ExecuteAsync_DoesNotFlag_WhenFileIsRecent()
    {
        // Arrange — file written 30min ago, threshold is 2h.
        var file = Path.Combine(_drive1Path, "fresh.metadata");
        CreateFileWithTimestamp(file, DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(30)));

        SetupLastSeen(DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(1)));

        // Act
        await InboxOrphanScan.ExecuteAsync(
            _loggerMock.Object, _inboxDrivesRoot, _ageThreshold, _lastSeenMock.Object, _hostOdinId);

        // Assert
        VerifyLog(LogLevel.Error, "Inbox orphan", Times.Never());
    }

    [Test]
    public void ExecuteAsync_RespectsStoppingToken()
    {
        // Arrange — orphan-eligible file, but token already cancelled.
        var orphan = Path.Combine(_drive1Path, "orphan.metadata");
        CreateFileWithTimestamp(orphan, DateTime.UtcNow.Subtract(TimeSpan.FromHours(5)));
        SetupLastSeen(DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(5)));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act / Assert
        Assert.ThrowsAsync<OperationCanceledException>(() =>
            InboxOrphanScan.ExecuteAsync(
                _loggerMock.Object, _inboxDrivesRoot, _ageThreshold, _lastSeenMock.Object, _hostOdinId, cts.Token));
    }

    [Test]
    public void ExecuteAsync_ReturnsSilently_WhenInboxDrivesPathDoesNotExist()
    {
        var nonExistent = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        Assert.DoesNotThrowAsync(() =>
            InboxOrphanScan.ExecuteAsync(
                _loggerMock.Object, nonExistent, _ageThreshold, _lastSeenMock.Object, _hostOdinId));

        // GetLastSeenAsync should not even be called when the path doesn't exist.
        _lastSeenMock.Verify(s => s.GetLastSeenAsync(It.IsAny<OdinId>()), Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_OnlyFlagsOrphanFiles_MixedFolderContents()
    {
        // Arrange — three files in the same drive folder:
        //   1. old + tenant seen after threshold      -> orphan
        //   2. old + tenant seen too recently to have processed it -> NOT orphan
        //   3. recent                                 -> NOT orphan
        var oldFileSeenAfter = Path.Combine(_drive1Path, "orphan.metadata");
        var oldFileSeenTooRecently = Path.Combine(_drive1Path, "borderline.payload");
        var freshFile = Path.Combine(_drive1Path, "fresh.thumb");

        CreateFileWithTimestamp(oldFileSeenAfter,        DateTime.UtcNow.Subtract(TimeSpan.FromHours(6)));
        CreateFileWithTimestamp(oldFileSeenTooRecently,  DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(150))); // 2.5h
        CreateFileWithTimestamp(freshFile,               DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(15)));

        // Tenant seen 1 hour ago.
        // - oldFileSeenAfter:       written 6h ago → fileWrite + 2h = 4h ago. lastSeen (1h ago) > 4h ago → orphan.
        // - oldFileSeenTooRecently: written 2.5h ago → fileWrite + 2h = 0.5h ago. lastSeen (1h ago) NOT > 0.5h ago → not orphan.
        SetupLastSeen(DateTime.UtcNow.Subtract(TimeSpan.FromHours(1)));

        // Act
        await InboxOrphanScan.ExecuteAsync(
            _loggerMock.Object, _inboxDrivesRoot, _ageThreshold, _lastSeenMock.Object, _hostOdinId);

        // Assert — exactly one orphan log line
        VerifyLog(LogLevel.Error, "Inbox orphan:", Times.Once());
        VerifyLog(LogLevel.Error, "Inbox orphan summary:", Times.Once());
        Assert.That(File.Exists(oldFileSeenAfter), Is.True);
        Assert.That(File.Exists(oldFileSeenTooRecently), Is.True);
        Assert.That(File.Exists(freshFile), Is.True);
    }

    //

    private void SetupLastSeen(DateTime utc)
    {
        _lastSeenMock
            .Setup(s => s.GetLastSeenAsync(It.IsAny<OdinId>()))
            .ReturnsAsync((UnixTimeUtc?)UnixTimeUtc.FromDateTime(utc));
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
