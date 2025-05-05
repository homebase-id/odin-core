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
    private string _tempRootPath = "";
    private string _uploadsPath = "";
    private string _inboxPath = "";
    private readonly Mock<ILogger> _loggerMock = new ();

    [SetUp]
    public void SetUp()
    {
        // Create a temporary directory for testing
        _tempRootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempRootPath);

        // Create uploads and inbox subdirectories
        _uploadsPath = Path.Combine(_tempRootPath, "uploads");
        _inboxPath = Path.Combine(_tempRootPath, "inbox");
        Directory.CreateDirectory(_uploadsPath);
        Directory.CreateDirectory(_inboxPath);
    }

    [TearDown]
    public void TearDown()
    {
        // Clean up temporary directory after each test
        try
        {
            if (Directory.Exists(_tempRootPath))
            {
                Directory.Delete(_tempRootPath, true);
            }
        }
        catch (IOException)
        {
            // Files might still be locked, try again after a short delay
            Thread.Sleep(100);
            if (Directory.Exists(_tempRootPath))
            {
                Directory.Delete(_tempRootPath, true);
            }
        }
    }

    [Test]
    public void Execute_WithValidParameters_DeletesOldFiles()
    {
        // Arrange
        var oldUploadFile = Path.Combine(_uploadsPath, "old_upload.txt");
        var newUploadFile = Path.Combine(_uploadsPath, "new_upload.txt");
        var oldInboxFile = Path.Combine(_inboxPath, "old_inbox.txt");
        var newInboxFile = Path.Combine(_inboxPath, "new_inbox.txt");

        File.WriteAllText(oldUploadFile, "test");
        File.WriteAllText(newUploadFile, "test");
        File.WriteAllText(oldInboxFile, "test");
        File.WriteAllText(newInboxFile, "test");

        // Set file times
        File.SetLastWriteTimeUtc(oldUploadFile, DateTime.UtcNow.AddHours(-2));
        File.SetLastWriteTimeUtc(newUploadFile, DateTime.UtcNow);
        File.SetLastWriteTimeUtc(oldInboxFile, DateTime.UtcNow.AddHours(-3));
        File.SetLastWriteTimeUtc(newInboxFile, DateTime.UtcNow);

        var uploadThreshold = TimeSpan.FromHours(1);
        var inboxThreshold = TimeSpan.FromHours(2);

        // Act
        TempFolderCleanUp.Execute(_loggerMock.Object, _tempRootPath, uploadThreshold, inboxThreshold);

        // Assert
        Assert.That(File.Exists(oldUploadFile), Is.False, "Old upload file should be deleted");
        Assert.That(File.Exists(newUploadFile), Is.True, "New upload file should not be deleted");
        Assert.That(File.Exists(oldInboxFile), Is.False,"Old inbox file should be deleted");
        Assert.That(File.Exists(newInboxFile), Is.True, "New inbox file should not be deleted");
    }

    [Test]
    public void Execute_WithNonexistentFolder_ThrowsOdinSystemException()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        // Act & Assert
        Assert.Throws<OdinSystemException>(() =>
            TempFolderCleanUp.Execute(
                _loggerMock.Object,
                nonExistentPath,
                TimeSpan.FromHours(1),
                TimeSpan.FromHours(1)
            )
        );
    }

    [Test]
    public void Execute_WithNullOrEmptyTempFolder_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            TempFolderCleanUp.Execute(
                _loggerMock.Object,
                null,
                TimeSpan.FromHours(1),
                TimeSpan.FromHours(1)
            )
        );

        Assert.Throws<ArgumentException>(() =>
            TempFolderCleanUp.Execute(
                _loggerMock.Object,
                string.Empty,
                TimeSpan.FromHours(1),
                TimeSpan.FromHours(1)
            )
        );

        Assert.Throws<ArgumentException>(() =>
            TempFolderCleanUp.Execute(
                _loggerMock.Object,
                "  ",
                TimeSpan.FromHours(1),
                TimeSpan.FromHours(1)
            )
        );
    }

    [Test]
    public void Execute_WithNegativeUploadThreshold_ThrowsArgumentException()
    {
        // Arrange
        var negativeThreshold = TimeSpan.FromHours(-1);

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            TempFolderCleanUp.Execute(
                _loggerMock.Object,
                _tempRootPath,
                negativeThreshold,
                TimeSpan.FromHours(1)
            )
        );

        Assert.That(ex?.Message, Does.Contain("Upload age threshold must be positive"));
    }

    [Test]
    public void Execute_WithNegativeInboxThreshold_ThrowsArgumentException()
    {
        // Arrange
        var negativeThreshold = TimeSpan.FromHours(-1);

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            TempFolderCleanUp.Execute(
                _loggerMock.Object,
                _tempRootPath,
                TimeSpan.FromHours(1),
                negativeThreshold
            )
        );

        Assert.That(ex?.Message, Does.Contain("Inbox age threshold must be positive"));
    }

    [Test]
    public void Execute_WithZeroUploadThreshold_ThrowsArgumentException()
    {
        // Arrange
        var zeroThreshold = TimeSpan.Zero;

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            TempFolderCleanUp.Execute(
                _loggerMock.Object,
                _tempRootPath,
                zeroThreshold,
                TimeSpan.FromHours(1)
            )
        );

        Assert.That(ex?.Message, Does.Contain("Upload age threshold must be positive"));
    }

    [Test]
    public void Execute_WithMissingSubdirectories_CreatesNoErrors()
    {
        // Arrange
        Directory.Delete(_uploadsPath);
        Directory.Delete(_inboxPath);

        // Act - should not throw
        TempFolderCleanUp.Execute(
            _loggerMock.Object,
            _tempRootPath,
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(1)
        );
    }

    [Test]
    public void Execute_WithIllegalSubdirectories_LogsError()
    {
        // Arrange
        var illegalSubDir = Path.Combine(_uploadsPath, "illegal");
        Directory.CreateDirectory(illegalSubDir);

        // Act
        TempFolderCleanUp.Execute(
            _loggerMock.Object,
            _tempRootPath,
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(1)
        );

        // Assert
        // Verify error was logged
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Illegal subdirectories")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()!
            ),
            Times.Once
        );
    }

}