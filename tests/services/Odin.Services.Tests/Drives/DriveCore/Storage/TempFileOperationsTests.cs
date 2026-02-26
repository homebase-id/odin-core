using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core.Time;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;

namespace Odin.Services.Tests.Drives.DriveCore.Storage;

public class TempFileOperationsTests
{
    private string _testRootPath = string.Empty;
    private OdinConfiguration _config = null!;
    private FileReaderWriter _fileReaderWriter = null!;
    private Mock<IDriveManager> _driveManagerMock = null!;
    private Mock<ILogger> _loggerMock = null!;
    private TenantPathManager _tenantPathManager = null!;

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
                TenantDataRootPath = _testRootPath
            }
        };

        _fileReaderWriter = new FileReaderWriter(_config, new Mock<ILogger<FileReaderWriter>>().Object);
        _driveManagerMock = new Mock<IDriveManager>();
        _loggerMock = new Mock<ILogger>();
        _tenantPathManager = new TenantPathManager(_config, Guid.NewGuid());
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
    public async Task CleanupFiles_ShouldDeleteFiles()
    {
        // Arrange
        var driveId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var fileIdObj = new InternalDriveFileId(driveId, fileId);
        var descriptors = new List<PayloadDescriptor>
        {
            new() { Key = "key1", Uid = UnixTimeUtcUnique.Now(), Thumbnails = new List<ThumbnailDescriptor> { new() { PixelWidth = 100, PixelHeight = 100 } } }
        };

        var drive = new StorageDrive(_tenantPathManager, new StorageDriveData { Id = driveId });
        _driveManagerMock.Setup(dm => dm.GetDriveAsync(driveId, false)).ReturnsAsync(drive);

        // Create dummy files
        var payloadExtension = TenantPathManager.GetBasePayloadFileNameAndExtension(descriptors[0].Key, descriptors[0].Uid);
        var payloadPath = Path.Combine(_testRootPath, TenantPathManager.GetFilename(fileId, payloadExtension));
        var thumbnailExtension = TenantPathManager.GetThumbnailFileNameAndExtension(descriptors[0].Key, descriptors[0].Uid, 100, 100);
        var thumbnailPath = Path.Combine(_testRootPath, TenantPathManager.GetFilename(fileId, thumbnailExtension));
        await File.WriteAllTextAsync(payloadPath, "payload");
        await File.WriteAllTextAsync(thumbnailPath, "thumbnail");

        // Act
        await TempFileOperations.CleanupFiles(_fileReaderWriter, _driveManagerMock.Object, _loggerMock.Object,
            fileIdObj, descriptors, (d, fid, ext) => Path.Combine(_testRootPath, TenantPathManager.GetFilename(fid.FileId, ext)), "test files");

        // Assert
        Assert.That(File.Exists(payloadPath), Is.False);
        Assert.That(File.Exists(thumbnailPath), Is.False);
        _driveManagerMock.Verify(dm => dm.GetDriveAsync(driveId, false), Times.Once);
    }

    [Test]
    public void GetPathFromDrive_ShouldReturnPath()
    {
        // Arrange
        var driveId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var fileIdObj = new InternalDriveFileId(driveId, fileId);
        const string extension = ".txt";

        var drive = new StorageDrive(_tenantPathManager, new StorageDriveData { Id = driveId });

        // Act
        var result = TempFileOperations.GetPathFromDrive(drive, fileIdObj, extension, d => _testRootPath);

        // Assert
        var expectedPath = Path.Combine(_testRootPath, TenantPathManager.GetFilename(fileId, extension));
        Assert.That(result, Is.EqualTo(expectedPath));
    }

    [Test]
    public void GetPathFromDrive_ShouldCreateDirectory_WhenEnsureExists()
    {
        // Arrange
        var driveId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var fileIdObj = new InternalDriveFileId(driveId, fileId);
        const string extension = ".txt";
        var subDir = Path.Combine(_testRootPath, "subdir");

        var drive = new StorageDrive(_tenantPathManager, new StorageDriveData { Id = driveId });

        // Act
        var result = TempFileOperations.GetPathFromDrive(drive, fileIdObj, extension, d => subDir, true);

        // Assert
        Assert.That(Directory.Exists(subDir), Is.True);
        var expectedPath = Path.Combine(subDir, TenantPathManager.GetFilename(fileId, extension));
        Assert.That(result, Is.EqualTo(expectedPath));
    }
}