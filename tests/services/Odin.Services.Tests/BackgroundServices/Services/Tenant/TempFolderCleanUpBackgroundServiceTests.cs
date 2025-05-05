using System;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Services.Background.BackgroundServices.Tenant;

namespace Odin.Services.Tests.BackgroundServices.Services.Tenant;

[TestFixture]
public class TempFolderCleanUpBackgroundServiceTests
{
    private string _testTempRoot = "";
    private Mock<ILogger> _loggerMock = new ();
    private TimeSpan _uploadAgeThreshold;
    private TimeSpan _inboxAgeThreshold;

    [SetUp]
    public void Setup()
    {
        // Create a unique temp folder for testing
        _testTempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testTempRoot);

        // Define thresholds for testing
        _uploadAgeThreshold = TimeSpan.FromHours(1);
        _inboxAgeThreshold = TimeSpan.FromHours(2);
    }

    [TearDown]
    public void TearDown()
    {
        // Clean up test directory after test
        if (Directory.Exists(_testTempRoot))
        {
            try
            {
                Directory.Delete(_testTempRoot, true);
            }
            catch (IOException)
            {
                // If we can't delete it immediately, schedule it for deletion on process exit
                Thread.Sleep(100); // Give system a moment to release file handles
                try
                {
                    Directory.Delete(_testTempRoot, true);
                }
                catch
                {
                    Console.WriteLine($"Warning: Could not delete temp directory {_testTempRoot}");
                }
            }
        }
    }

    [Test]
    public void Execute_DeletesOldFiles_PreservesRecentFiles()
    {
        // Arrange
        SetupTestFolderStructure();

        string drive1UploadsPath = Path.Combine(_testTempRoot, "drives", "drive1", "uploads");
        string drive1InboxPath = Path.Combine(_testTempRoot, "drives", "drive1", "inbox");

        // Create old files that should be deleted
        string oldUploadFile = Path.Combine(drive1UploadsPath, "old_upload.txt");
        string oldInboxFile = Path.Combine(drive1InboxPath, "old_inbox.txt");

        // Create new files that should be preserved
        string newUploadFile = Path.Combine(drive1UploadsPath, "new_upload.txt");
        string newInboxFile = Path.Combine(drive1InboxPath, "new_inbox.txt");

        CreateFileWithTimestamp(oldUploadFile, DateTime.UtcNow.Subtract(_uploadAgeThreshold).Subtract(TimeSpan.FromMinutes(30)));
        CreateFileWithTimestamp(oldInboxFile, DateTime.UtcNow.Subtract(_inboxAgeThreshold).Subtract(TimeSpan.FromMinutes(30)));
        CreateFileWithTimestamp(newUploadFile, DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(10)));
        CreateFileWithTimestamp(newInboxFile, DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(10)));

        // Act
        TempFolderCleanUp.Execute(
            _loggerMock.Object,
            _testTempRoot,
            _uploadAgeThreshold,
            _inboxAgeThreshold
        );

        // Assert
        Assert.That(File.Exists(oldUploadFile), Is.False, "Old upload file should be deleted");
        Assert.That(File.Exists(oldInboxFile), Is.False, "Old inbox file should be deleted");
        Assert.That(File.Exists(newUploadFile), Is.True,"Recent upload file should be preserved");
        Assert.That(File.Exists(newInboxFile), Is.True,"Recent inbox file should be preserved");
    }

    [Test]
    public void Execute_HandlesIllegalSubdirectories()
    {
        // Arrange
        SetupTestFolderStructure();

        string drive1UploadsPath = Path.Combine(_testTempRoot, "drives", "drive1", "uploads");

        // Create illegal subdirectory
        string illegalSubdir = Path.Combine(drive1UploadsPath, "illegal_subdir");
        Directory.CreateDirectory(illegalSubdir);

        // Act
        TempFolderCleanUp.Execute(
            _loggerMock.Object,
            _testTempRoot,
            _uploadAgeThreshold,
            _inboxAgeThreshold
        );

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
    public void Execute_RespectsStoppingToken()
    {
        // Arrange
        SetupTestFolderStructure();

        string drive1UploadsPath = Path.Combine(_testTempRoot, "drives", "drive1", "uploads");
        string oldUploadFile = Path.Combine(drive1UploadsPath, "old_upload.txt");
        CreateFileWithTimestamp(oldUploadFile, DateTime.UtcNow.Subtract(_uploadAgeThreshold).Subtract(TimeSpan.FromMinutes(30)));

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel token immediately

        // Act
        TempFolderCleanUp.Execute(
            _loggerMock.Object,
            _testTempRoot,
            _uploadAgeThreshold,
            _inboxAgeThreshold,
            cts.Token
        );

        // Assert
        Assert.That(File.Exists(oldUploadFile), Is.True, "File should not be deleted when token is cancelled");
    }

    [Test]
    public void Execute_ThrowsException_WhenTempFolderDoesNotExist()
    {
        // Arrange
        string nonExistentFolder = Path.Combine(_testTempRoot, "non_existent_folder");

        // Act & Assert
        var ex = Assert.Throws<OdinSystemException>(() => TempFolderCleanUp.Execute(
            _loggerMock.Object,
            nonExistentFolder,
            _uploadAgeThreshold,
            _inboxAgeThreshold
        ));

        Assert.That(ex?.Message, Does.Contain("does not exist"));
    }

    [Test]
    public void Execute_ThrowsException_WhenThresholdsAreNegative()
    {
        // Act & Assert - Negative upload threshold
        var ex1 = Assert.Throws<ArgumentException>(() => TempFolderCleanUp.Execute(
            _loggerMock.Object,
            _testTempRoot,
            TimeSpan.FromSeconds(-1),
            _inboxAgeThreshold
        ));

        Assert.That(ex1?.Message, Does.Contain("Upload age threshold"));

        // Act & Assert - Negative inbox threshold
        var ex2 = Assert.Throws<ArgumentException>(() => TempFolderCleanUp.Execute(
            _loggerMock.Object,
            _testTempRoot,
            _uploadAgeThreshold,
            TimeSpan.FromSeconds(-1)
        ));

        Assert.That(ex2?.Message, Does.Contain("Inbox age threshold"));
    }

    private void SetupTestFolderStructure()
    {
        // Create the base folders structure
        string drivesFolder = Path.Combine(_testTempRoot, "drives");
        Directory.CreateDirectory(drivesFolder);

        // Create two drive folders
        string drive1 = Path.Combine(drivesFolder, "drive1");
        string drive2 = Path.Combine(drivesFolder, "drive2");
        Directory.CreateDirectory(drive1);
        Directory.CreateDirectory(drive2);

        // Create uploads and inbox folders for each drive
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

