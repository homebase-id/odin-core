using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;

namespace Odin.Services.Tests.Drives.DriveCore.Storage;

public class PayloadFileReaderWriterTests : PayloadReaderWriterBaseTestFixture
{
    private OdinConfiguration _config = null!;

    private TenantContext _tenantContext = null!;
    private TenantPathManager _tenantPathManager = null!;

    private readonly Mock<ILogger<PayloadFileReaderWriter>> _loggerMock = new();
    private FileReaderWriter _fileReaderWriter = null!;

    [SetUp]
    public void Setup()
    {
        BaseSetup();

        _config = new OdinConfiguration
        {
            Host = new OdinConfiguration.HostSection
            {
                TenantDataRootPath = Path.Combine(TestRootPath, "tenants"),
                FileOperationRetryAttempts = 1,
                FileOperationRetryDelayMs = TimeSpan.FromMilliseconds(1),
            }
        };

        var tenantId = Guid.NewGuid();
        _tenantContext = new TenantContext(
            tenantId,
            new OdinId("frodo.me"),
            new TenantPathManager(_config, tenantId),
            firstRunToken: null,
            isPreconfigured: true,
            markedForDeletionDate: null
        );
        _tenantPathManager = _tenantContext.TenantPathManager;

        var logger = new Mock<ILogger<FileReaderWriter>>();
        _fileReaderWriter = new FileReaderWriter(_config, logger.Object);
   }

    //

    [TearDown]
    public void TearDown()
    {
        try
        {
            // more stuff
        }
        finally
        {
            BaseTearDown();
        }
    }

    //

    [Test]
    public async Task WriteFileAsync_ShouldWriteFile()
    {
        var driveId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var appKey = "testAppKey";
        var timestamp = UnixTimeUtcUnique.Now();

        var rw = new PayloadFileReaderWriter(_loggerMock.Object, _tenantContext, _fileReaderWriter);

        var path = _tenantPathManager.GetPayloadDirectoryAndFileName(driveId, fileId, appKey, timestamp);
        var someBytes = "hello".ToUtf8ByteArray();
        await rw.WriteFileAsync(path, someBytes);

        Assert.That(File.Exists(path), Is.True);

        var readBytes = await File.ReadAllBytesAsync(path);
        Assert.That(readBytes, Is.EqualTo(someBytes));
    }

    //

    [Test]
    public async Task DeleteFileAsync_ShouldDeleteFile()
    {
        var driveId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var appKey = "testAppKey";
        var timestamp = UnixTimeUtcUnique.Now();

        var rw = new PayloadFileReaderWriter(_loggerMock.Object, _tenantContext, _fileReaderWriter);

        var path = _tenantPathManager.GetPayloadDirectoryAndFileName(driveId, fileId, appKey, timestamp);
        var someBytes = "hello".ToUtf8ByteArray();
        await rw.WriteFileAsync(path, someBytes);

        Assert.That(File.Exists(path), Is.True);

        await rw.DeleteFileAsync(path);
        Assert.That(File.Exists(path), Is.False);
    }

    //

    [Test]
    public async Task FileExistsAsync_ShouldCheckIfFileExists()
    {
        var driveId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var appKey = "testAppKey";
        var timestamp = UnixTimeUtcUnique.Now();

        var rw = new PayloadFileReaderWriter(_loggerMock.Object, _tenantContext, _fileReaderWriter);

        var path = _tenantPathManager.GetPayloadDirectoryAndFileName(driveId, fileId, appKey, timestamp);
        var someBytes = "hello".ToUtf8ByteArray();
        await rw.WriteFileAsync(path, someBytes);

        var exists = await rw.FileExistsAsync(path);
        Assert.That(exists, Is.True);

        await rw.DeleteFileAsync(path);
        exists = await rw.FileExistsAsync(path);
        Assert.That(exists, Is.False);
    }

    //

    [Test]
    public async Task MoveFileAsync_ShouldMoveTheFile()
    {
        var driveId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var appKey = "testAppKey";
        var timestamp = UnixTimeUtcUnique.Now();

        var rw = new PayloadFileReaderWriter(_loggerMock.Object, _tenantContext, _fileReaderWriter);

        var srcPath = _tenantPathManager.GetPayloadDirectoryAndFileName(driveId, fileId, appKey, timestamp);
        var someBytes = "hello".ToUtf8ByteArray();
        await rw.WriteFileAsync(srcPath, someBytes);

        var exists = await rw.FileExistsAsync(srcPath);
        Assert.That(exists, Is.True);

        var otherDriveId = Guid.NewGuid();
        var dstPath = _tenantPathManager.GetPayloadDirectoryAndFileName(otherDriveId, fileId, appKey, timestamp);
        exists = await rw.FileExistsAsync(dstPath);
        Assert.That(exists, Is.False);

        await rw.MoveFileAsync(srcPath, dstPath);

        exists = await rw.FileExistsAsync(srcPath);
        Assert.That(exists, Is.False);

        exists = await rw.FileExistsAsync(dstPath);
        Assert.That(exists, Is.True);
    }

    //

    [Test]
    public async Task MoveFileAsync_ShouldThrowOnMissingSrcFile()
    {
        var driveId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var appKey = "testAppKey";
        var timestamp = UnixTimeUtcUnique.Now();

        var rw = new PayloadFileReaderWriter(_loggerMock.Object, _tenantContext, _fileReaderWriter);

        var srcPath = _tenantPathManager.GetPayloadDirectoryAndFileName(driveId, fileId, appKey, timestamp);
        var someBytes = "hello".ToUtf8ByteArray();
        await rw.WriteFileAsync(srcPath, someBytes);

        var exists = await rw.FileExistsAsync(srcPath);
        Assert.That(exists, Is.True);

        var otherDriveId = Guid.NewGuid();
        var dstPath = _tenantPathManager.GetPayloadDirectoryAndFileName(otherDriveId, fileId, appKey, timestamp);
        exists = await rw.FileExistsAsync(dstPath);
        Assert.That(exists, Is.False);

        var srcFile = Path.Combine(TestRootPath, Guid.NewGuid().ToString());
        var dstFile = Path.Combine(TestRootPath, Guid.NewGuid().ToString());

        Assert.ThrowsAsync<FileNotFoundException>(() => rw.MoveFileAsync(srcFile, dstFile));
    }





}