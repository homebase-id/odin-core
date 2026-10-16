using System;
using System.Collections.Concurrent;
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
/// appData.content, or only a <see cref="FileRef"/>.
///
/// In the WebScaffold framework (not the fast <c>Odin.Hosting.Tests.V2</c>) because it receives over a real
/// WebSocket, which the fast framework does not host.
/// </summary>
[TestFixture]
public class DriveNotificationShapeTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(15);
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
        var socket = await ShapedSocket.ConnectAsync(owner, drive, FileResultShape.Minimal);

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

            Assert.That(Describe(added), Is.EqualTo($"header:null fileRef:{file.FileId}/{upload.Content.NewVersionTag}/active"));
            Assert.That(Describe(modified), Is.EqualTo($"header:null fileRef:{file.FileId}/{update.Content!.NewVersionTag}/active"));
            Assert.That(Describe(softDeleted), Is.EqualTo(
                $"header:null fileRef:{file.FileId}/{tombstoneTag}/deleted previousFileRef:{file.FileId}/{update.Content.NewVersionTag}/active"));
            Assert.That(Describe(hardDeleted), Is.EqualTo($"header:null previousFileRef:{file.FileId}/{tombstoneTag}/deleted"));
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
        var full = await ShapedSocket.ConnectAsync(owner, drive, null);
        var noContent = await ShapedSocket.ConnectAsync(owner, drive, FileResultShape.NoContent);

        try
        {
            var upload = await owner.DriveRedux.UploadNewMetadata(drive, SampleMetadataData.CreateWithContent(100, "the content"));
            Assert.That(upload.IsSuccessStatusCode, Is.True, $"upload failed: {upload.StatusCode}");

            var fullEvent = await full.NextAsync(ClientNotificationType.FileAdded);
            var noContentEvent = await noContent.NextAsync(ClientNotificationType.FileAdded);

            // Both sockets share one event: emptying the content for one must not empty it for the other
            Assert.That((Content(fullEvent), Content(noContentEvent)), Is.EqualTo(("the content", "")));
            Assert.That(Keys(fullEvent), Has.None.AnyOf("fileRef", "previousFileRef"), fullEvent.ToString());
        }
        finally
        {
            await full.DisconnectAsync();
            await noContent.DisconnectAsync();
        }
    }

    [Test]
    public async Task AnUnknownShapeIsRefusedAtTheHandshake()
    {
        var (owner, drive) = await SetupDriveAsync();
        var listener = new TestOwnerWebSocketListener();

        var refused = Assert.ThrowsAsync<Exception>(() => listener.ConnectAsync(owner.Identity.OdinId, owner.GetTokenContext(),
            new EstablishConnectionOptions { Drives = [drive], ResultShape = (FileResultShape)7 }));

        Assert.That(refused!.Message, Is.EqualTo("Did not receive a valid handshake"));
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
    /// The header's presence and each ref as fileId/versionTag/fileState, so one assertion shows the whole event
    /// </summary>
    private static string Describe(JsonElement data)
    {
        var parts = new List<string> { data.TryGetProperty("header", out var h) && h.ValueKind != JsonValueKind.Null ? "header:set" : "header:null" };
        foreach (var name in new[] { "fileRef", "previousFileRef" })
        {
            if (data.TryGetProperty(name, out var r))
            {
                parts.Add($"{name}:{r.GetProperty("fileId").GetGuid()}/{r.GetProperty("versionTag").GetGuid()}/{r.GetProperty("fileState").GetString()}");
            }
        }

        return string.Join(" ", parts);
    }

    private static string Content(JsonElement data) =>
        data.GetProperty("header").GetProperty("fileMetadata").GetProperty("appData").GetProperty("content").GetString();

    private static List<string> Keys(JsonElement data) => data.EnumerateObject().Select(p => p.Name).ToList();

    /// <summary>
    /// An owner notification socket that asked for <paramref name="shape"/>, keeping each drive event's data as JSON
    /// </summary>
    private sealed class ShapedSocket
    {
        private readonly TestOwnerWebSocketListener _listener = new();
        private readonly ConcurrentQueue<(ClientNotificationType Type, JsonElement Data)> _events = new();

        public static async Task<ShapedSocket> ConnectAsync(OwnerApiClientRedux owner, TargetDrive drive, FileResultShape? shape)
        {
            var socket = new ShapedSocket();
            socket._listener.NotificationReceived += n =>
            {
                if (n.NotificationType is ClientNotificationType.FileAdded or ClientNotificationType.FileModified
                    or ClientNotificationType.FileDeleted)
                {
                    socket._events.Enqueue((n.NotificationType, JsonDocument.Parse(n.Data).RootElement.Clone()));
                }

                return Task.CompletedTask;
            };
            await socket._listener.ConnectAsync(owner.Identity.OdinId, owner.GetTokenContext(),
                new EstablishConnectionOptions { Drives = [drive], ResultShape = shape });
            return socket;
        }

        /// <summary>
        /// The next drive event, which must be of <paramref name="expected"/> type
        /// </summary>
        public async Task<JsonElement> NextAsync(ClientNotificationType expected)
        {
            var deadline = DateTime.UtcNow + WaitTimeout;
            (ClientNotificationType Type, JsonElement Data) next;
            while (!_events.TryDequeue(out next))
            {
                if (DateTime.UtcNow > deadline)
                {
                    Assert.Fail($"no {expected} arrived within {WaitTimeout.TotalSeconds} s");
                }

                await Task.Delay(50);
            }

            Assert.That(next.Type, Is.EqualTo(expected), next.Data.ToString());
            return next.Data;
        }

        public Task DisconnectAsync() => _listener.DisconnectAsync();
    }
}
