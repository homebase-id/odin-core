using System;
using System.IO;
using System.Text;
using System.Threading;
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
    private FileReaderWriter _fileReaderWriter = null!;
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
                FileWriteChunkSizeInBytes = 4096,
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

        _fileReaderWriter = new FileReaderWriter(_config, new Mock<ILogger<FileReaderWriter>>().Object);
        var inboxFileStore = new InboxFileStore(new DiskFileStore(_fileReaderWriter));
        _sut = new InboxStorageManager(inboxFileStore, new Mock<ILogger<InboxStorageManager>>().Object, _tenantContext);
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

    [Test]
    public async Task WriteInboxStream_RoundTrips_Through_Exists_And_GetBytes()
    {
        var file = new InternalDriveFileId { DriveId = Guid.NewGuid(), FileId = Guid.NewGuid() };
        var ext = TenantPathManager.MetadataExtension;
        var bytes = Encoding.UTF8.GetBytes("hello inbox");

        // Drive inbox dir intentionally not pre-created: WriteInboxStream must create it.
        // The three methods must also agree on the path built from (DriveId, FileId, extension).
        Assert.That(Directory.Exists(_tenantPathManager.GetDriveInboxPath(file.DriveId)), Is.False);
        Assert.That(await _sut.InboxFileExists(file, ext), Is.False);

        using var stream = new MemoryStream(bytes);
        var written = await _sut.WriteInboxStream(file, ext, stream);

        Assert.That(written, Is.EqualTo(bytes.Length));
        Assert.That(await _sut.InboxFileExists(file, ext), Is.True);
        Assert.That(await _sut.GetAllInboxFileBytes(file, ext), Is.EqualTo(bytes));
    }

    [Test]
    public async Task CleanupInboxFiles_Swallows_ReaderException()
    {
        var innerStore = new Mock<IDriveFileStore>();
        innerStore.Setup(x => x.DeleteSetAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DriveFileStoreException("boom"));
        var inboxFileStore = new InboxFileStore(innerStore.Object);
        var sut = new InboxStorageManager(inboxFileStore, new Mock<ILogger<InboxStorageManager>>().Object, _tenantContext);

        var file = new InternalDriveFileId { DriveId = Guid.NewGuid(), FileId = Guid.NewGuid() };

        // Best-effort cleanup: a failure in the underlying store must be swallowed, not propagated.
        await sut.CleanupInboxFiles(file);

        innerStore.Verify(x => x.DeleteSetAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
