using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;

namespace Odin.Services.Tests.Drives.DriveCore.Storage;

public class UploadTempStorageTests
{
    private string _testRootPath = string.Empty;
    private OdinConfiguration _config = null!;
    private Mock<IDriveManager> _driveManagerMock = null!;
    private Mock<ILogger<UploadTempStorage>> _loggerMock = null!;
    private Mock<TenantContext> _tenantContextMock = null!;
    private TenantPathManager _tenantPathManager = null!;
    private UploadTempStorage _uploadTempStorage = null!;

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

        var fileReaderWriter = new FileReaderWriter(_config, new Mock<ILogger<FileReaderWriter>>().Object);
        _driveManagerMock = new Mock<IDriveManager>();
        _loggerMock = new Mock<ILogger<UploadTempStorage>>();
        var config = new OdinConfiguration { Host = new OdinConfiguration.HostSection { FileOperationRetryAttempts = 1, FileOperationRetryDelayMs = TimeSpan.FromMilliseconds(1), TenantDataRootPath = _testRootPath } };
        _tenantPathManager = new TenantPathManager(config, Guid.NewGuid());
        var tenantContext = new TenantContext();
        typeof(TenantContext).GetProperty("TenantPathManager")!.SetValue(tenantContext, _tenantPathManager);

        _uploadTempStorage = new UploadTempStorage(
            fileReaderWriter,
            _driveManagerMock.Object,
            _loggerMock.Object,
            tenantContext);
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
    public async Task FileExists_ShouldReturnTrue_WhenFileExists()
    {
        // Arrange
        var driveId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var file = new UploadFile(new InternalDriveFileId(driveId, fileId));
        const string extension = ".txt";

        var drive = new StorageDrive(_tenantPathManager, new StorageDriveData { Id = driveId });
        _driveManagerMock.Setup(dm => dm.GetDriveAsync(driveId, false)).ReturnsAsync(drive);

        var path = await _uploadTempStorage.GetPath(file, extension);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "test");

        // Act
        var result = await _uploadTempStorage.FileExists(file, extension);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task FileExists_ShouldReturnFalse_WhenFileDoesNotExist()
    {
        // Arrange
        var driveId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var file = new UploadFile(new InternalDriveFileId(driveId, fileId));
        const string extension = ".txt";

        var drive = new StorageDrive(_tenantPathManager, new StorageDriveData { Id = driveId });
        _driveManagerMock.Setup(dm => dm.GetDriveAsync(driveId, false)).ReturnsAsync(drive);

        // Act
        var result = await _uploadTempStorage.FileExists(file, extension);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task GetAllFileBytes_ShouldReturnBytes()
    {
        // Arrange
        var driveId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var file = new UploadFile(new InternalDriveFileId(driveId, fileId));
        const string extension = ".txt";
        var expectedBytes = new byte[] { 1, 2, 3 };

        var drive = new StorageDrive(_tenantPathManager, new StorageDriveData { Id = driveId });
        _driveManagerMock.Setup(dm => dm.GetDriveAsync(driveId, false)).ReturnsAsync(drive);

        var path = await _uploadTempStorage.GetPath(file, extension);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, expectedBytes);

        // Act
        var result = await _uploadTempStorage.GetAllFileBytes(file, extension);

        // Assert
        Assert.That(result, Is.EqualTo(expectedBytes));
    }


    [Test]
    public async Task CleanupUploadedFiles_ShouldDeleteFiles()
    {
        // Arrange
        var driveId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var file = new UploadFile(new InternalDriveFileId(driveId, fileId));
        var descriptors = new List<PayloadDescriptor>
        {
            new() { Key = "key1", Uid = UnixTimeUtcUnique.Now(), Thumbnails = new List<ThumbnailDescriptor>() }
        };

        var drive = new StorageDrive(_tenantPathManager, new StorageDriveData { Id = driveId });
        _driveManagerMock.Setup(dm => dm.GetDriveAsync(driveId, false)).ReturnsAsync(drive);

        var drivePath = _tenantPathManager.GetDriveUploadPath(driveId);
        Directory.CreateDirectory(drivePath);

        // Create dummy files
        var payloadExtension = TenantPathManager.GetBasePayloadFileNameAndExtension(descriptors[0].Key, descriptors[0].Uid);
        var payloadPath = Path.Combine(drivePath, TenantPathManager.GetFilename(file.FileId.FileId, payloadExtension));
        await File.WriteAllTextAsync(payloadPath, "payload");

        // Act
        await _uploadTempStorage.CleanupUploadedFiles(file, descriptors);

        // Assert
        Assert.That(File.Exists(payloadPath), Is.False);
    }


}