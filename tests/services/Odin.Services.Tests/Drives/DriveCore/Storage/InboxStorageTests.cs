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

public class InboxStorageTests
{
    private string _testRootPath = string.Empty;
    private OdinConfiguration _config = null!;
    private FileReaderWriter _fileReaderWriter = null!;
    private Mock<IDriveManager> _driveManagerMock = null!;
    private Mock<ILogger<InboxStorage>> _loggerMock = null!;
    private TenantPathManager _tenantPathManager = null!;
    private InboxStorage _inboxStorage = null!;

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

        _fileReaderWriter = new FileReaderWriter(_config, new Mock<ILogger<FileReaderWriter>>().Object);
        _driveManagerMock = new Mock<IDriveManager>();
        _loggerMock = new Mock<ILogger<InboxStorage>>();
        var config = new OdinConfiguration { Host = new OdinConfiguration.HostSection { FileOperationRetryAttempts = 1, FileOperationRetryDelayMs = TimeSpan.FromMilliseconds(1), TenantDataRootPath = _testRootPath } };
        _tenantPathManager = new TenantPathManager(config, Guid.NewGuid());
        var tenantContext = new TenantContext();
        typeof(TenantContext).GetProperty("TenantPathManager")!.SetValue(tenantContext, _tenantPathManager);

        _inboxStorage = new InboxStorage(
            _fileReaderWriter,
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
        var file = new InboxFile(new InternalDriveFileId(driveId, fileId));
        const string extension = ".txt";

        var drive = new StorageDrive(_tenantPathManager, new StorageDriveData { Id = driveId });
        _driveManagerMock.Setup(dm => dm.GetDriveAsync(driveId, false)).ReturnsAsync(drive);

        var path = await _inboxStorage.GetPath(file, extension);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "test");

        // Act
        var result = await _inboxStorage.FileExists(file, extension);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task FileExists_ShouldReturnFalse_WhenFileDoesNotExist()
    {
        // Arrange
        var driveId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var file = new InboxFile(new InternalDriveFileId(driveId, fileId));
        const string extension = ".txt";

        var drive = new StorageDrive(_tenantPathManager, new StorageDriveData { Id = driveId });
        _driveManagerMock.Setup(dm => dm.GetDriveAsync(driveId, false)).ReturnsAsync(drive);

        // Act
        var result = await _inboxStorage.FileExists(file, extension);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task GetAllFileBytes_ShouldReturnBytes()
    {
        // Arrange
        var driveId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var file = new InboxFile(new InternalDriveFileId(driveId, fileId));
        const string extension = ".txt";
        var expectedBytes = new byte[] { 1, 2, 3 };

        var drive = new StorageDrive(_tenantPathManager, new StorageDriveData { Id = driveId });
        _driveManagerMock.Setup(dm => dm.GetDriveAsync(driveId, false)).ReturnsAsync(drive);

        var path = await _inboxStorage.GetPath(file, extension);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, expectedBytes);

        // Act
        var result = await _inboxStorage.GetAllFileBytes(file, extension);

        // Assert
        Assert.That(result, Is.EqualTo(expectedBytes));
    }


    [Test]
    public async Task CleanupInboxFiles_ShouldDeleteFiles()
    {
        // Arrange
        var driveId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var file = new InboxFile(new InternalDriveFileId(driveId, fileId));
        var descriptors = new List<PayloadDescriptor>
        {
            new() { Key = "key1", Uid = UnixTimeUtcUnique.Now(), Thumbnails = new List<ThumbnailDescriptor>() }
        };

        var drive = new StorageDrive(_tenantPathManager, new StorageDriveData { Id = driveId });
        _driveManagerMock.Setup(dm => dm.GetDriveAsync(driveId, false)).ReturnsAsync(drive);

        var drivePath = _tenantPathManager.GetDriveInboxStoragePath(driveId);
        Directory.CreateDirectory(drivePath);

        // Create dummy files
        var payloadExtension = TenantPathManager.GetBasePayloadFileNameAndExtension(descriptors[0].Key, descriptors[0].Uid);
        var payloadPath = Path.Combine(drivePath, TenantPathManager.GetFilename(fileId, payloadExtension));
        var metadataPath = Path.Combine(drivePath, TenantPathManager.GetFilename(fileId, TenantPathManager.MetadataExtension));
        var transferPath = Path.Combine(drivePath, TenantPathManager.GetFilename(fileId, TenantPathManager.TransferInstructionSetExtension));
        await File.WriteAllTextAsync(payloadPath, "payload");
        await File.WriteAllTextAsync(metadataPath, "metadata");
        await File.WriteAllTextAsync(transferPath, "transfer");

        // Act
        await _inboxStorage.CleanupInboxFiles(file, descriptors);

        // Assert
        Assert.That(File.Exists(payloadPath), Is.False);
        Assert.That(File.Exists(metadataPath), Is.False);
        Assert.That(File.Exists(transferPath), Is.False);
    }


}