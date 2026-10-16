using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.ApiClient;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests._V2.ApiClient.TestCases;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests._V2.Tests.Drive.WriteFileTests;

/// <summary>
/// A notification socket chooses at its handshake what drive events describe their file with
/// (<see cref="EstablishConnectionOptions.ResultShape"/>): the full header (the default), the header without
/// appData.content, or only a <see cref="FileRef"/>. Whatever the shape, a delete names the file it removed.
///
/// In the WebScaffold framework (not the fast <c>Odin.Hosting.Tests.V2</c>) because it receives over a real
/// WebSocket, which the fast framework does not host.
/// </summary>
[TestFixture]
public class DriveNotificationShapeTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _scaffold = new WebScaffold(GetType().Name);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity> { TestIdentities.Pippin });
    }

    [OneTimeTearDown]
    public void OneTimeTearDown() => _scaffold.RunAfterAnyTests();

    [SetUp]
    public void Setup()
    {
        _scaffold.ClearAssertLogEventsAction();
        _scaffold.ClearLogEvents();
    }

    [TearDown]
    public void TearDown() => _scaffold.AssertLogEvents();

    [Test]
    public async Task AMinimalSocketGetsAFileRefForEveryDriveEvent()
    {
        var (owner, drive) = await SetupDriveAsync();
        var socket = await ConnectAsync(owner, drive, FileResultShape.Minimal);

        try
        {
            var upload = await owner.DriveRedux.UploadNewMetadata(drive, SampleMetadataData.CreateWithContent(100, "first"));
            Assert.That(upload.IsSuccessStatusCode, Is.True, $"upload failed: {upload.StatusCode}");
            var file = upload.Content!.File;
            var added = await socket.NextAsync(ClientNotificationType.FileAdded);

            var update = await owner.DriveRedux.UpdateExistingMetadata(file, upload.Content.NewVersionTag,
                SampleMetadataData.CreateWithContent(100, "second"));
            Assert.That(update.IsSuccessStatusCode, Is.True, $"update failed: {update.StatusCode}");
            var modified = await socket.NextAsync(ClientNotificationType.FileModified);

            var softDelete = await owner.DriveRedux.SoftDeleteFile(file);
            Assert.That(softDelete.IsSuccessStatusCode, Is.True, $"soft delete failed: {softDelete.StatusCode}");
            var softDeleted = await socket.NextAsync(ClientNotificationType.FileDeleted);

            // The tombstone's tag, as a minimal client learns it: from the event
            var tombstoneTag = softDeleted.GetProperty("fileRef").GetProperty("versionTag").GetGuid();
            await HardDeleteAsync(owner, drive, file.FileId, tombstoneTag);
            var hardDeleted = await socket.NextAsync(ClientNotificationType.FileDeleted);

            // Only what the event carries: no header keys, and no null for an absent ref
            Assert.That(Describe(added), Is.EqualTo($"targetDrive fileRef:{file.FileId}/{upload.Content.NewVersionTag}/active"));
            Assert.That(Describe(modified), Is.EqualTo($"targetDrive fileRef:{file.FileId}/{update.Content!.NewVersionTag}/active"));
            Assert.That(Describe(softDeleted), Is.EqualTo(
                $"targetDrive fileRef:{file.FileId}/{tombstoneTag}/deleted previousFileRef:{file.FileId}/{update.Content.NewVersionTag}/active"));
            Assert.That(Describe(hardDeleted), Is.EqualTo($"targetDrive previousFileRef:{file.FileId}/{tombstoneTag}/deleted"));
        }
        finally
        {
            await socket.DisconnectAsync();
        }
    }

    [Test]
    public async Task EachSocketGetsTheShapeItAskedFor()
    {
        var (owner, drive) = await SetupDriveAsync();
        DriveEventSocket full = null, noContent = null;

        try
        {
            full = await ConnectAsync(owner, drive, null);
            noContent = await ConnectAsync(owner, drive, FileResultShape.NoContent);

            var upload = await owner.DriveRedux.UploadNewMetadata(drive, SampleMetadataData.CreateWithContent(100, "the content"));
            Assert.That(upload.IsSuccessStatusCode, Is.True, $"upload failed: {upload.StatusCode}");
            var fullAdded = await full.NextAsync(ClientNotificationType.FileAdded);
            var noContentAdded = await noContent.NextAsync(ClientNotificationType.FileAdded);

            var softDelete = await owner.DriveRedux.SoftDeleteFile(upload.Content!.File);
            Assert.That(softDelete.IsSuccessStatusCode, Is.True, $"soft delete failed: {softDelete.StatusCode}");
            var fullDeleted = await full.NextAsync(ClientNotificationType.FileDeleted);
            var noContentDeleted = await noContent.NextAsync(ClientNotificationType.FileDeleted);

            // Both sockets share one event: emptying the content for one must not empty it for the other
            Assert.That((Content(fullAdded), Content(noContentAdded)), Is.EqualTo(("the content", "")));
            Assert.That((Content(fullDeleted, "previousServerFileHeader"), Content(noContentDeleted, "previousServerFileHeader")),
                Is.EqualTo(("the content", "")));
            Assert.That(Keys(fullAdded), Has.None.AnyOf("fileRef", "previousFileRef"), fullAdded.ToString());
        }
        finally
        {
            await (full?.DisconnectAsync() ?? Task.CompletedTask);
            await (noContent?.DisconnectAsync() ?? Task.CompletedTask);
        }
    }

    [Test]
    public async Task AFullSocketIsToldWhichFileAHardDeleteRemoved()
    {
        var (owner, drive) = await SetupDriveAsync();
        var socket = await ConnectAsync(owner, drive, null);

        try
        {
            var upload = await owner.DriveRedux.UploadNewMetadata(drive, SampleMetadataData.Create(fileType: 100));
            Assert.That(upload.IsSuccessStatusCode, Is.True, $"upload failed: {upload.StatusCode}");
            await socket.NextAsync(ClientNotificationType.FileAdded);

            await HardDeleteAsync(owner, drive, upload.Content!.File.FileId, upload.Content.NewVersionTag);
            var deleted = await socket.NextAsync(ClientNotificationType.FileDeleted);

            // As a soft delete does: nothing is left (header null), and the header the file had names it
            var previous = deleted.GetProperty("previousServerFileHeader");
            // The alias is serialized without dashes, which JsonElement.GetGuid does not read
            Assert.That(Guid.Parse(deleted.GetProperty("targetDrive").GetProperty("alias").GetString()!), Is.EqualTo(drive.Alias.Value));
            Assert.That((deleted.GetProperty("header").ValueKind, previous.GetProperty("fileId").GetGuid(),
                    previous.GetProperty("fileMetadata").GetProperty("globalTransitId").GetGuid()),
                Is.EqualTo((JsonValueKind.Null, upload.Content.File.FileId, upload.Content.GlobalTransitId!.Value)), deleted.ToString());
        }
        finally
        {
            await socket.DisconnectAsync();
        }
    }

    [Test]
    public async Task AnUnknownShapeIsRefusedAtTheHandshake()
    {
        var (owner, drive) = await SetupDriveAsync();
        var listener = new TestOwnerWebSocketListener();

        var refused = Assert.ThrowsAsync<Exception>(() => listener.ConnectAsync(owner.Identity.OdinId, owner.GetTokenContext(),
            new EstablishConnectionOptions { Drives = [drive], ResultShape = (FileResultShape)7 }));
        await listener.DisconnectAsync();

        Assert.That(refused!.Message, Does.StartWith("Did not receive a valid handshake: Error").And.Contain("Unknown resultShape 7"));
    }

    // ---------------------------------------------------------------------------------------------

    private async Task<(OwnerApiClientRedux owner, TargetDrive drive)> SetupDriveAsync()
    {
        var owner = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Pippin);
        var drive = TargetDrive.NewTargetDrive();
        await owner.DriveManager.CreateDrive(drive, "Notification shape test drive", "", allowAnonymousReads: true);
        return (owner, drive);
    }

    private static async Task HardDeleteAsync(OwnerApiClientRedux owner, TargetDrive drive, Guid fileId, Guid versionTag)
    {
        var callerContext = new OwnerTestCase(drive);
        await callerContext.Initialize(owner);
        var writer = new DriveWriterV2Client(owner.Identity.OdinId, callerContext.GetFactory());
        var response = await writer.HardDeleteBatch(drive.Alias, new HardDeleteBatchRequestV2
        {
            Files = [new FileVersionTagV2 { FileId = fileId, VersionTag = versionTag }]
        });
        Assert.That((response.IsSuccessStatusCode, response.Content?.Results.Single().ErrorCode),
            Is.EqualTo((true, (Odin.Core.Exceptions.OdinClientErrorCode?)null)), response.Error?.Content);
    }

    /// <summary>
    /// Every key of the event, each ref as fileId/versionTag/fileState, so one assertion shows the whole event
    /// </summary>
    private static string Describe(JsonElement data) =>
        string.Join(" ", data.EnumerateObject().Select(p => p.Name is "fileRef" or "previousFileRef"
            ? $"{p.Name}:{p.Value.GetProperty("fileId").GetGuid()}/{p.Value.GetProperty("versionTag").GetGuid()}/{p.Value.GetProperty("fileState").GetString()}"
            : p.Name));

    private static string Content(JsonElement data, string header = "header") =>
        data.GetProperty(header).GetProperty("fileMetadata").GetProperty("appData").GetProperty("content").GetString();

    private static List<string> Keys(JsonElement data) => data.EnumerateObject().Select(p => p.Name).ToList();

    private static Task<DriveEventSocket> ConnectAsync(OwnerApiClientRedux owner, TargetDrive drive, FileResultShape? shape) =>
        DriveEventSocket.ConnectOwnerAsync(owner.Identity.OdinId, owner.GetTokenContext(),
            new EstablishConnectionOptions { Drives = [drive], ResultShape = shape });
}
