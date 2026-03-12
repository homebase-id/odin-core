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

// Tests for UploadFileHandler
public class UploadFileHandlerTests
{
    private string _testRootPath = string.Empty;
    private OdinConfiguration _config = null!;
    private Mock<IDriveManager> _driveManagerMock = null!;
    private Mock<ILogger<UploadFileHandler>> _loggerMock = null!;
    private TenantContext _tenantContext = null!;
    private UploadFileHandler _uploadFileHandler = null!;
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
        _loggerMock = new Mock<ILogger<UploadFileHandler>>();

        var tenantContextMock = new Mock<TenantContext>();

        _tenantContext = tenantContextMock.Object;

        var dummyConfig = new OdinConfiguration { Host = new() { TenantDataRootPath = "/dummy" } };
        var dummyTenantId = Guid.NewGuid();
        var dummyTenantPathManager = new TenantPathManager(dummyConfig, dummyTenantId);
        var dummyData = new StorageDriveData { Id = Guid.NewGuid(), Name = "dummy" };
        _storageDriveMock = new Mock<StorageDrive>(dummyTenantPathManager, dummyData);
        _storageDriveMock.Setup(d => d.GetDriveUploadPath()).Returns(Path.Combine(_testRootPath, "uploads"));

        _uploadFileHandler = new UploadFileHandler(
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
    public async Task GetPath_UploadType_ReturnsCorrectPath()
    {
        // Arrange
        var fileId = Guid.Parse("12345678-1234-1234-1234-123456789012");
        var tempFile = new TempFile
        {
            File = new InternalDriveFileId { DriveId = Guid.NewGuid(), FileId = fileId },
            StorageType = TempStorageType.Upload
        };
        _driveManagerMock.Setup(dm => dm.GetDriveAsync(It.IsAny<Guid>(), It.IsAny<bool>())).ReturnsAsync(_storageDriveMock.Object);

        // Act
        var path = await _uploadFileHandler.GetPath(tempFile, ".payload");

        // Assert
        var expectedPath = Path.Combine(_testRootPath, "uploads", "12345678123412341234123456789012..payload");
        Assert.That(path, Is.EqualTo(expectedPath));
    }

    [Test]
    public async Task TempFileExists_ReturnsTrue_WhenFileExists()
    {
        // Arrange
        var tempFile = new TempFile
        {
            File = new InternalDriveFileId { DriveId = Guid.NewGuid(), FileId = Guid.Parse("12345678-1234-1234-1234-123456789012") },
            StorageType = TempStorageType.Upload
        };
        _driveManagerMock.Setup(dm => dm.GetDriveAsync(It.IsAny<Guid>(), It.IsAny<bool>())).ReturnsAsync(_storageDriveMock.Object);

        var fileReaderWriter = new FileReaderWriter(_config, new Mock<ILogger<FileReaderWriter>>().Object);
        var handler = new UploadFileHandler(
            fileReaderWriter,
            _driveManagerMock.Object,
            _loggerMock.Object,
            _tenantContext);

        // Create the file
        var path = Path.Combine(_testRootPath, "uploads", "12345678123412341234123456789012..payload");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "test");

        // Act
        var result = await handler.TempFileExists(tempFile, ".payload");

        // Assert
        Assert.That(result, Is.True);

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
            StorageType = TempStorageType.Upload
        };
        _driveManagerMock.Setup(dm => dm.GetDriveAsync(It.IsAny<Guid>(), It.IsAny<bool>())).ReturnsAsync(_storageDriveMock.Object);

        var fileReaderWriter = new FileReaderWriter(_config, new Mock<ILogger<FileReaderWriter>>().Object);
        var handler = new UploadFileHandler(
            fileReaderWriter,
            _driveManagerMock.Object,
            _loggerMock.Object,
            _tenantContext);

        // Act
        var path = await handler.GetPath(tempFile, "");

        // Assert
        var expected = Path.Combine(_testRootPath, "uploads", "12345678123412341234123456789012");
        Assert.That(path, Is.EqualTo(expected));
    }

    [Test]
    public async Task TempFileExists_FileNotFound_ReturnsFalse()
    {
        // Arrange
        var tempFile = new TempFile
        {
            File = new InternalDriveFileId { DriveId = Guid.NewGuid(), FileId = Guid.Parse("12345678-1234-1234-1234-123456789012") },
            StorageType = TempStorageType.Upload
        };
        _driveManagerMock.Setup(dm => dm.GetDriveAsync(It.IsAny<Guid>(), It.IsAny<bool>())).ReturnsAsync(_storageDriveMock.Object);

        var fileReaderWriter = new FileReaderWriter(_config, new Mock<ILogger<FileReaderWriter>>().Object);
        var handler = new UploadFileHandler(
            fileReaderWriter,
            _driveManagerMock.Object,
            _loggerMock.Object,
            _tenantContext);

        // Act (file does not exist)
        var result = await handler.TempFileExists(tempFile, ".payload");

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
            StorageType = TempStorageType.Upload
        };
        _driveManagerMock.Setup(dm => dm.GetDriveAsync(It.IsAny<Guid>(), It.IsAny<bool>())).ReturnsAsync(_storageDriveMock.Object);

        var fileReaderWriter = new FileReaderWriter(_config, new Mock<ILogger<FileReaderWriter>>().Object);
        var handler = new UploadFileHandler(
            fileReaderWriter,
            _driveManagerMock.Object,
            _loggerMock.Object,
            _tenantContext);

        // Act & Assert
        Assert.ThrowsAsync<OdinSystemException>(async () => await handler.GetAllFileBytes(tempFile, ".payload"));
    }

    [Test]
    public async Task GetPath_InvalidFileId_HandlesGracefully()
    {
        // Arrange
        var tempFile = new TempFile
        {
            File = new InternalDriveFileId { DriveId = Guid.NewGuid(), FileId = Guid.Empty },
            StorageType = TempStorageType.Upload
        };
        _driveManagerMock.Setup(dm => dm.GetDriveAsync(It.IsAny<Guid>(), It.IsAny<bool>())).ReturnsAsync(_storageDriveMock.Object);

        var fileReaderWriter = new FileReaderWriter(_config, new Mock<ILogger<FileReaderWriter>>().Object);
        var handler = new UploadFileHandler(
            fileReaderWriter,
            _driveManagerMock.Object,
            _loggerMock.Object,
            _tenantContext);

        // Act
        var path = await handler.GetPath(tempFile, ".payload");

        // Assert (should generate path without crashing)
        Assert.That(path, Does.Contain("00000000000000000000000000000000..payload"));
    }

}
