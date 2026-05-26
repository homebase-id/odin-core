using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;

namespace Odin.Services.Tests.Drives.DriveCore.Storage;

public class InboxStorageManagerTests : PayloadReaderWriterBaseTestFixture
{
    private OdinConfiguration _config = null!;
    private TenantContext _tenantContext = null!;
    private TenantPathManager _tenantPathManager = null!;
    private InboxStorageManager _sut = null!;

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
            markedForDeletionDate: null,
            email: null);
        _tenantPathManager = _tenantContext.TenantPathManager;

        var fileReaderWriter = new FileReaderWriter(_config, new Mock<ILogger<FileReaderWriter>>().Object);
        var inboxReaderWriter = new InboxFileReaderWriter(fileReaderWriter);
        _sut = new InboxStorageManager(inboxReaderWriter, new Mock<ILogger<InboxStorageManager>>().Object, _tenantContext);
    }

    [TearDown]
    public void TearDown() => BaseTearDown();

    [Test]
    public async Task CleanupInboxFiles_DeletesAllStagingFilesForFileId()
    {
        // Repro: PeerInboxProcessor catches exceptions during inbox processing and used to call
        // CleanupInboxFiles with an empty descriptor list, which short-circuited and left
        // .payload/.thumb files behind (e.g. yossi.silberberg.dk leaked 4 convo_img staging
        // files). The glob-based cleanup deletes every {fileId:N}.* entry in the drive inbox dir,
        // independent of whatever descriptors the in-flight processor managed to parse.
        var file = new InternalDriveFileId { DriveId = Guid.NewGuid(), FileId = Guid.NewGuid() };
        var driveInboxDir = _tenantPathManager.GetDriveInboxPath(file.DriveId);
        Directory.CreateDirectory(driveInboxDir);

        var metadata = _tenantPathManager.GetDriveInboxFilePath(file.DriveId, file.FileId, TenantPathManager.MetadataExtension);
        var keyHeader = _tenantPathManager.GetDriveInboxFilePath(file.DriveId, file.FileId, TenantPathManager.TransferInstructionSetExtension);
        var payload  = Path.Combine(driveInboxDir, $"{file.FileId:N}.convo_img-116589220245667840.payload");
        var thumb320 = Path.Combine(driveInboxDir, $"{file.FileId:N}.convo_img-116589220245667840-320x320.thumb");
        var thumb640 = Path.Combine(driveInboxDir, $"{file.FileId:N}.convo_img-116589220245667840-640x640.thumb");

        foreach (var p in new[] { metadata, keyHeader, payload, thumb320, thumb640 })
            File.WriteAllText(p, "x");

        await _sut.CleanupInboxFiles(file);

        Assert.That(File.Exists(metadata), Is.False);
        Assert.That(File.Exists(keyHeader), Is.False);
        Assert.That(File.Exists(payload),  Is.False);
        Assert.That(File.Exists(thumb320), Is.False);
        Assert.That(File.Exists(thumb640), Is.False);
    }

    [Test]
    public async Task CleanupInboxFiles_LeavesOtherFileIdsAlone()
    {
        // The cleanup is scoped to one fileId. Files for a different fileId in the same drive
        // inbox dir must not be touched.
        var driveId = Guid.NewGuid();
        var targetFile = new InternalDriveFileId { DriveId = driveId, FileId = Guid.NewGuid() };
        var otherFileId = Guid.NewGuid();

        var driveInboxDir = _tenantPathManager.GetDriveInboxPath(driveId);
        Directory.CreateDirectory(driveInboxDir);

        var targetPayload = Path.Combine(driveInboxDir, $"{targetFile.FileId:N}.convo_img-1.payload");
        var otherPayload  = Path.Combine(driveInboxDir, $"{otherFileId:N}.convo_img-2.payload");
        File.WriteAllText(targetPayload, "x");
        File.WriteAllText(otherPayload, "x");

        await _sut.CleanupInboxFiles(targetFile);

        Assert.That(File.Exists(targetPayload), Is.False);
        Assert.That(File.Exists(otherPayload),  Is.True, "Files for a different fileId must not be deleted");
    }

    [Test]
    public async Task CleanupInboxFiles_NoThrow_WhenDriveInboxDirMissing()
    {
        // No staging dir at all (no transfers ever staged) — must be a no-op, not a throw.
        var file = new InternalDriveFileId { DriveId = Guid.NewGuid(), FileId = Guid.NewGuid() };
        Assert.That(Directory.Exists(_tenantPathManager.GetDriveInboxPath(file.DriveId)), Is.False);

        await _sut.CleanupInboxFiles(file);
    }
}
