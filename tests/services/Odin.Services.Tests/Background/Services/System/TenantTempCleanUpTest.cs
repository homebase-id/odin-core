using System;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Services.Background.Services.System;

namespace Odin.Services.Tests.Background.Services.System;

public class TenantTempCleanUpTest
{
    private string _testDir = "";
    private readonly Mock<ILogger> _loggerMock = new Mock<ILogger>();

    [SetUp]
    public void SetUp()
    {
        // Create a unique temp directory for each test
        _testDir = Path.Combine(Path.GetTempPath(), $"TenantTempCleanUpTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);

    }

    [TearDown]
    public void TearDown()
    {
        // Cleanup test directory
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, true);
        }
    }

    [Test]
    public void Execute_DirectoryDoesNotExist_LogsDebugAndExits()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_testDir, "nonexistent");
        var deleteOlderThan = TimeSpan.FromHours(24);

        // Act
        TenantTempCleanUp.Execute(_loggerMock.Object, nonExistentPath, deleteOlderThan);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(nonExistentPath)),
                null,
                It.IsAny<Func<It.IsAnyType, Exception, string>>()!),
            Times.Once());
        // No files should be deleted, no other logs
    }

    [Test]
    public void Execute_NoFilesInDirectory_DoesNothing()
    {
        // Arrange
        var deleteOlderThan = TimeSpan.FromHours(24);

        // Act
        TenantTempCleanUp.Execute(_loggerMock.Object, _testDir, deleteOlderThan);

        // Assert
        Assert.That(Directory.GetFiles(_testDir), Is.Empty);
        _loggerMock.VerifyNoOtherCalls(); // No logs except potentially debug if added
    }

    [Test]
    public void Execute_DeletesFilesOlderThanThreshold()
    {
        // Arrange
        var deleteOlderThan = TimeSpan.FromHours(24);
        var oldFile = CreateTestFile("old.txt", DateTime.Now.AddHours(-48)); // 48 hours old
        var newFile = CreateTestFile("new.txt", DateTime.Now.AddHours(-12)); // 12 hours old

        // Act
        TenantTempCleanUp.Execute(_loggerMock.Object, _testDir, deleteOlderThan);

        // Assert
        Assert.That(File.Exists(oldFile), Is.False, "Old file should be deleted");
        Assert.That(File.Exists(newFile), Is.True, "New file should remain");
    }

    [Test]
    public void Execute_CancellationRequested_StopsBeforeDeletion()
    {
        // Arrange
        var deleteOlderThan = TimeSpan.FromHours(24);
        var oldFile = CreateTestFile("old.txt", DateTime.Now.AddHours(-48));
        var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act
        TenantTempCleanUp.Execute(_loggerMock.Object, _testDir, deleteOlderThan, cts.Token);

        // Assert
        Assert.That(File.Exists(oldFile), Is.True, "File should not be deleted due to cancellation");
    }

    [Test]
    public void Execute_DeletesMultipleEligibleFiles()
    {
        // Arrange
        var deleteOlderThan = TimeSpan.FromHours(24);
        var oldFile1 = CreateTestFile("old1.txt", DateTime.Now.AddHours(-48));
        var oldFile2 = CreateTestFile("old2.txt", DateTime.Now.AddHours(-36));
        var newFile = CreateTestFile("new.txt", DateTime.Now.AddHours(-12));

        // Act
        TenantTempCleanUp.Execute(_loggerMock.Object, _testDir, deleteOlderThan);

        // Assert
        Assert.That(File.Exists(oldFile1), Is.False, "Old file 1 should be deleted");
        Assert.That(File.Exists(oldFile2), Is.False, "Old file 2 should be deleted");
        Assert.That(File.Exists(newFile), Is.True, "New file should remain");
    }

    private string CreateTestFile(string fileName, DateTime creationTime)
    {
        var filePath = Path.Combine(_testDir, fileName);
        File.WriteAllText(filePath, "Test content");
        File.SetCreationTime(filePath, creationTime);
        return filePath;
    }
}