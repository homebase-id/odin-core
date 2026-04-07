using System;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Services.Background.BackgroundServices.Tenant;

namespace Odin.Services.Tests.BackgroundServices.Services.Tenant;

[TestFixture]
public class TempFolderCleanUpBackgroundServiceTests
{
    private string _testDrivesRoot = "";
    private Mock<ILogger> _loggerMock = new ();
    private TimeSpan _uploadAgeThreshold;
    private TimeSpan _inboxAgeThreshold;

    [SetUp]
    public void Setup()
    {
        // Create a unique drives folder for testing (mirrors UploadDrivesPath / InboxDrivesPath)
        _testDrivesRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "drives");
        Directory.CreateDirectory(_testDrivesRoot);

        _uploadAgeThreshold = TimeSpan.FromHours(1);
        _inboxAgeThreshold = TimeSpan.FromHours(2);
    }

    [TearDown]
    public void TearDown()
    {
        var root = Path.GetDirectoryName(_testDrivesRoot)!;
        if (Directory.Exists(root))
        {
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
    }

    [Test]
    public void UploadFolderCleanUp_DeletesOldFiles_PreservesRecentFiles()
    {
        // Arrange
        SetupTestFolderStructure();

        string drive1UploadsPath = Path.Combine(_testDrivesRoot, "drive1", "uploads");

        string oldFile = Path.Combine(drive1UploadsPath, "old_upload.txt");
        string newFile = Path.Combine(drive1UploadsPath, "new_upload.txt");

        CreateFileWithTimestamp(oldFile, DateTime.UtcNow.Subtract(_uploadAgeThreshold).Subtract(TimeSpan.FromMinutes(30)));
        CreateFileWithTimestamp(newFile, DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(10)));

        // Act
        UploadFolderCleanUp.Execute(_loggerMock.Object, _testDrivesRoot, _uploadAgeThreshold);

        // Assert
        Assert.That(File.Exists(oldFile), Is.False, "Old upload file should be deleted");
        Assert.That(File.Exists(newFile), Is.True, "Recent upload file should be preserved");
    }

    [Test]
    public void InboxFolderCleanUp_DeletesOldFiles_PreservesRecentFiles()
    {
        // Arrange
        SetupTestFolderStructure();

        string drive1InboxPath = Path.Combine(_testDrivesRoot, "drive1", "inbox");

        string oldFile = Path.Combine(drive1InboxPath, "old_inbox.txt");
        string newFile = Path.Combine(drive1InboxPath, "new_inbox.txt");

        CreateFileWithTimestamp(oldFile, DateTime.UtcNow.Subtract(_inboxAgeThreshold).Subtract(TimeSpan.FromMinutes(30)));
        CreateFileWithTimestamp(newFile, DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(10)));

        // Act
        InboxFolderCleanUp.Execute(_loggerMock.Object, _testDrivesRoot, _inboxAgeThreshold);

        // Assert
        Assert.That(File.Exists(oldFile), Is.False, "Old inbox file should be deleted");
        Assert.That(File.Exists(newFile), Is.True, "Recent inbox file should be preserved");
    }

    [Test]
    public void UploadFolderCleanUp_HandlesIllegalSubdirectories()
    {
        // Arrange
        SetupTestFolderStructure();

        string drive1UploadsPath = Path.Combine(_testDrivesRoot, "drive1", "uploads");
        Directory.CreateDirectory(Path.Combine(drive1UploadsPath, "illegal_subdir"));

        // Act
        UploadFolderCleanUp.Execute(_loggerMock.Object, _testDrivesRoot, _uploadAgeThreshold);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Illegal subdirectories detected")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()!
            ),
            Times.AtLeastOnce
        );
    }

    [Test]
    public void UploadFolderCleanUp_RespectsStoppingToken()
    {
        // Arrange
        SetupTestFolderStructure();

        string drive1UploadsPath = Path.Combine(_testDrivesRoot, "drive1", "uploads");
        string oldFile = Path.Combine(drive1UploadsPath, "old_upload.txt");
        CreateFileWithTimestamp(oldFile, DateTime.UtcNow.Subtract(_uploadAgeThreshold).Subtract(TimeSpan.FromMinutes(30)));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        UploadFolderCleanUp.Execute(_loggerMock.Object, _testDrivesRoot, _uploadAgeThreshold, cts.Token);

        // Assert
        Assert.That(File.Exists(oldFile), Is.True, "File should not be deleted when token is cancelled");
    }

    [Test]
    public void UploadFolderCleanUp_ReturnsSilently_WhenDrivesPathDoesNotExist()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        // Act & Assert — should not throw
        Assert.DoesNotThrow(() =>
            UploadFolderCleanUp.Execute(_loggerMock.Object, nonExistentPath, _uploadAgeThreshold));
    }

    [Test]
    public void InboxFolderCleanUp_ReturnsSilently_WhenDrivesPathDoesNotExist()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        // Act & Assert — should not throw
        Assert.DoesNotThrow(() =>
            InboxFolderCleanUp.Execute(_loggerMock.Object, nonExistentPath, _inboxAgeThreshold));
    }

    private void SetupTestFolderStructure()
    {
        string drive1 = Path.Combine(_testDrivesRoot, "drive1");
        string drive2 = Path.Combine(_testDrivesRoot, "drive2");
        Directory.CreateDirectory(drive1);
        Directory.CreateDirectory(drive2);

        Directory.CreateDirectory(Path.Combine(drive1, "uploads"));
        Directory.CreateDirectory(Path.Combine(drive1, "inbox"));
        Directory.CreateDirectory(Path.Combine(drive2, "uploads"));
        Directory.CreateDirectory(Path.Combine(drive2, "inbox"));
    }

    private void CreateFileWithTimestamp(string filePath, DateTime timestamp)
    {
        File.WriteAllText(filePath, "Test content");
        File.SetCreationTimeUtc(filePath, timestamp);
        File.SetLastWriteTimeUtc(filePath, timestamp);
    }
}
