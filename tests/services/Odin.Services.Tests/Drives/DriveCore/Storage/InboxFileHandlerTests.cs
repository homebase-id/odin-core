using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;

namespace Odin.Services.Tests.Drives.DriveCore.Storage;

// Tests for InboxFileHandler
public class InboxFileHandlerTests
{
    private string _testRootPath = string.Empty;
    private OdinConfiguration _config = null!;
    private Mock<IDriveManager> _driveManagerMock = null!;
    private Mock<ILogger<InboxFileHandler>> _loggerMock = null!;
    private TenantContext _tenantContext = null!;
    private InboxFileHandler _inboxFileHandler = null!;
    private Mock<StorageDrive> _storageDriveMock = null!;

    [SetUp]
    public void Setup()
    {
        _testRootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testRootPath);

        _config = new OdinConfiguration
        {
            Host = new OdinConfiguration.HostSection
            {
                FileOperationRetryAttempts = 1,
                FileOperationRetryDelayMs = TimeSpan.FromMilliseconds(1),
            }
        };

        _driveManagerMock = new Mock<IDriveManager>();
        _loggerMock = new Mock<ILogger<InboxFileHandler>>();

        var tenantContextMock = new Mock<TenantContext>();

        _tenantContext = tenantContextMock.Object;

        var dummyConfig = new OdinConfiguration { Host = new() { TenantDataRootPath = "/dummy" } };
        var dummyTenantId = Guid.NewGuid();
        var dummyTenantPathManager = new TenantPathManager(dummyConfig, dummyTenantId);
        var dummyData = new StorageDriveData { Id = Guid.NewGuid(), Name = "dummy" };
        _storageDriveMock = new Mock<StorageDrive>(dummyTenantPathManager, dummyData);
        _storageDriveMock.Setup(d => d.GetDriveInboxPath()).Returns(Path.Combine(_testRootPath, "inbox"));

        _inboxFileHandler = new InboxFileHandler(
            new FileReaderWriter(_config, new Mock<ILogger<FileReaderWriter>>().Object),
            _driveManagerMock.Object,
            _loggerMock.Object,
            _tenantContext);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testRootPath))
        {
            Directory.Delete(_testRootPath, true);
        }
    }



    [Test]
    public async Task GetPath_InboxType_ReturnsCorrectPath()
    {
        // Arrange
        var fileId = Guid.Parse("12345678-1234-1234-1234-123456789012");
        var tempFile = new TempFile
        {
            File = new InternalDriveFileId { DriveId = Guid.NewGuid(), FileId = fileId },
            StorageType = TempStorageType.Inbox
        };
        _driveManagerMock.Setup(dm => dm.GetDriveAsync(It.IsAny<Guid>(), It.IsAny<bool>())).ReturnsAsync(_storageDriveMock.Object);

        // Act
        var path = await _inboxFileHandler.GetPath(tempFile, ".metadata");

        // Assert
        var expectedPath = Path.Combine(_testRootPath, "inbox", "12345678123412341234123456789012..metadata");
        Assert.That(path, Is.EqualTo(expectedPath));
    }

    [Test]
    public async Task TempFileExists_ReturnsTrue_WhenFileExists()
    {
        // Arrange
        var tempFile = new TempFile
        {
            File = new InternalDriveFileId { DriveId = Guid.NewGuid(), FileId = Guid.Parse("12345678-1234-1234-1234-123456789012") },
            StorageType = TempStorageType.Inbox
        };
        _driveManagerMock.Setup(dm => dm.GetDriveAsync(It.IsAny<Guid>(), It.IsAny<bool>())).ReturnsAsync(_storageDriveMock.Object);
        var fileReaderWriter = new FileReaderWriter(_config, new Mock<ILogger<FileReaderWriter>>().Object);
        var handler = new InboxFileHandler(
            fileReaderWriter,
            _driveManagerMock.Object,
            _loggerMock.Object,
            _tenantContext);

        // Create the file
        var path = Path.Combine(_testRootPath, "inbox", "12345678123412341234123456789012..metadata");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "test");

        // Act
        var result = await handler.TempFileExists(tempFile, ".metadata");

        // Assert
        Assert.That(result, Is.True);

        // Cleanup
        File.Delete(path);
    }

    [Test]
    public async Task GetAllFileBytes_ReturnsBytes_WhenFileReadSuccessfully()
    {
        // Arrange
        var tempFile = new TempFile
        {
            File = new InternalDriveFileId { DriveId = Guid.NewGuid(), FileId = Guid.Parse("12345678-1234-1234-1234-123456789012") },
            StorageType = TempStorageType.Inbox
        };
        var expectedBytes = new byte[] { 1, 2, 3 };
        _driveManagerMock.Setup(dm => dm.GetDriveAsync(It.IsAny<Guid>(), It.IsAny<bool>())).ReturnsAsync(_storageDriveMock.Object);

        var fileReaderWriter = new FileReaderWriter(_config, new Mock<ILogger<FileReaderWriter>>().Object);
        var handler = new InboxFileHandler(
            fileReaderWriter,
            _driveManagerMock.Object,
            _loggerMock.Object,
            _tenantContext);

        // Create the file with content
        var path = Path.Combine(_testRootPath, "inbox", "12345678123412341234123456789012..metadata");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, expectedBytes);

        // Act
        var result = await handler.GetAllFileBytes(tempFile, ".metadata");

        // Assert
        Assert.That(result, Is.EqualTo(expectedBytes));

        // Cleanup
        File.Delete(path);
    }

    [Test]
    public async Task GetPath_EmptyExtension_ReturnsCorrectPath()
    {
        // Arrange
        var tempFile = new TempFile
        {
            File = new InternalDriveFileId { DriveId = Guid.NewGuid(), FileId = Guid.Parse("12345678-1234-1234-1234-123456789012") },
            StorageType = TempStorageType.Inbox
        };
        _driveManagerMock.Setup(dm => dm.GetDriveAsync(It.IsAny<Guid>(), It.IsAny<bool>())).ReturnsAsync(_storageDriveMock.Object);

        var fileReaderWriter = new FileReaderWriter(_config, new Mock<ILogger<FileReaderWriter>>().Object);
        var handler = new InboxFileHandler(
            fileReaderWriter,
            _driveManagerMock.Object,
            _loggerMock.Object,
            _tenantContext);

        // Act
        var path = await handler.GetPath(tempFile, "");

        // Assert
        var expected = Path.Combine(_testRootPath, "inbox", "12345678123412341234123456789012");
        Assert.That(path, Is.EqualTo(expected));
    }

    [Test]
    public async Task TempFileExists_FileNotFound_ReturnsFalse()
    {
        // Arrange
        var tempFile = new TempFile
        {
            File = new InternalDriveFileId { DriveId = Guid.NewGuid(), FileId = Guid.Parse("12345678-1234-1234-1234-123456789012") },
            StorageType = TempStorageType.Inbox
        };
        _driveManagerMock.Setup(dm => dm.GetDriveAsync(It.IsAny<Guid>(), It.IsAny<bool>())).ReturnsAsync(_storageDriveMock.Object);

        var fileReaderWriter = new FileReaderWriter(_config, new Mock<ILogger<FileReaderWriter>>().Object);
        var handler = new InboxFileHandler(
            fileReaderWriter,
            _driveManagerMock.Object,
            _loggerMock.Object,
            _tenantContext);

        // Act (file does not exist)
        var result = await handler.TempFileExists(tempFile, ".metadata");

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void GetAllFileBytes_FileNotFound_ThrowsException()
    {
        // Arrange
        var tempFile = new TempFile
        {
            File = new InternalDriveFileId { DriveId = Guid.NewGuid(), FileId = Guid.Parse("12345678-1234-1234-1234-123456789012") },
            StorageType = TempStorageType.Inbox
        };
        _driveManagerMock.Setup(dm => dm.GetDriveAsync(It.IsAny<Guid>(), It.IsAny<bool>())).ReturnsAsync(_storageDriveMock.Object);

        var fileReaderWriter = new FileReaderWriter(_config, new Mock<ILogger<FileReaderWriter>>().Object);
        var handler = new InboxFileHandler(
            fileReaderWriter,
            _driveManagerMock.Object,
            _loggerMock.Object,
            _tenantContext);

        // Act & Assert
        Assert.ThrowsAsync<OdinSystemException>(async () => await handler.GetAllFileBytes(tempFile, ".metadata"));
    }

    [Test]
    public async Task GetPath_InvalidFileId_HandlesGracefully()
    {
        // Arrange
        var tempFile = new TempFile
        {
            File = new InternalDriveFileId { DriveId = Guid.NewGuid(), FileId = Guid.Empty },
            StorageType = TempStorageType.Inbox
        };
        _driveManagerMock.Setup(dm => dm.GetDriveAsync(It.IsAny<Guid>(), It.IsAny<bool>())).ReturnsAsync(_storageDriveMock.Object);

        var fileReaderWriter = new FileReaderWriter(_config, new Mock<ILogger<FileReaderWriter>>().Object);
        var handler = new InboxFileHandler(
            fileReaderWriter,
            _driveManagerMock.Object,
            _loggerMock.Object,
            _tenantContext);

        // Act
        var path = await handler.GetPath(tempFile, ".metadata");

        // Assert (should generate path without crashing)
        Assert.That(path, Does.Contain("00000000000000000000000000000000..metadata"));
    }


}