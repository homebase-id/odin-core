using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Tests._Universal.ApiClient;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests._V2.ApiClient.TestCases;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests._Universal.AppNotifications;

/// <summary>
/// A notification socket that asked to exclude its own writes (<see cref="EstablishConnectionOptions.ExcludeOwnWrites"/>)
/// gets no drive events for writes made with its token, and every other drive event as before: writes by other
/// tokens, and files arriving from peers whichever token's request or socket drained them from the inbox.
/// </summary>
public class ExcludeOwnWritesTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _scaffold = new WebScaffold(GetType().Name);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity> { TestIdentities.Frodo, TestIdentities.Samwise });
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
    public async Task EachSocketGetsEveryWriteButItsOwn()
    {
        var owner = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var drive = TargetDrive.NewTargetDrive();
        await owner.DriveManager.CreateDrive(drive, "Exclude own writes test drive", "", allowAnonymousReads: false);

        // Two devices: A writes with the owner client's token, B with a login of its own
        var tokenA = owner.GetTokenContext();
        var tokenB = await _scaffold.OldOwnerApi.LoginAnotherOwnerSessionAsync(owner.Identity.OdinId);
        var driveB = new UniversalDriveApiClient(owner.Identity.OdinId,
            new OwnerApiClientFactory(tokenB.AuthenticationResult, tokenB.SharedSecret.GetKey()));

        var aExcluding = await ConnectAsync(owner, tokenA, drive, excludeOwnWrites: true);
        var bExcluding = await ConnectAsync(owner, tokenB, drive, excludeOwnWrites: true);
        var aAll = await ConnectAsync(owner, tokenA, drive, excludeOwnWrites: false);

        try
        {
            // A: add, modify, soft delete (V1), hard delete (V2); then B adds one; then A adds one
            var f1 = await UploadAsync(owner.DriveRedux, drive);
            var update = await owner.DriveRedux.UpdateExistingMetadata(f1.File, f1.NewVersionTag, SampleMetadataData.Create(fileType: 100));
            Assert.That(update.IsSuccessStatusCode, Is.True, $"update failed: {update.StatusCode}");
            var softDelete = await owner.DriveRedux.SoftDeleteFile(f1.File);
            Assert.That(softDelete.IsSuccessStatusCode, Is.True, $"soft delete failed: {softDelete.StatusCode}");
            var tombstone = await owner.DriveRedux.GetFileHeader(f1.File);
            await HardDeleteAsync(owner, drive, f1.File.FileId, tombstone.Content!.FileMetadata.VersionTag);
            var f2 = await UploadAsync(driveB, drive);
            var f3 = await UploadAsync(owner.DriveRedux, drive);

            var fromA = $"fileAdded {f1.File.FileId}, fileModified {f1.File.FileId}, fileDeleted {f1.File.FileId}, fileDeleted {f1.File.FileId}";
            Assert.That(await NextEventsAsync(aExcluding, ClientNotificationType.FileAdded),
                Is.EqualTo($"fileAdded {f2.File.FileId}"), "A excludes its own writes: B's is the first it gets");
            Assert.That(await NextEventsAsync(bExcluding, ClientNotificationType.FileAdded, ClientNotificationType.FileModified,
                    ClientNotificationType.FileDeleted, ClientNotificationType.FileDeleted, ClientNotificationType.FileAdded),
                Is.EqualTo($"{fromA}, fileAdded {f3.File.FileId}"), "B gets all of A's writes and none of its own");
            Assert.That(await NextEventsAsync(aAll, ClientNotificationType.FileAdded, ClientNotificationType.FileModified,
                    ClientNotificationType.FileDeleted, ClientNotificationType.FileDeleted, ClientNotificationType.FileAdded,
                    ClientNotificationType.FileAdded),
                Is.EqualTo($"{fromA}, fileAdded {f2.File.FileId}, fileAdded {f3.File.FileId}"), "without the option nothing is left out");
        }
        finally
        {
            await aExcluding.DisconnectAsync();
            await bExcluding.DisconnectAsync();
            await aAll.DisconnectAsync();
        }
    }

    [Test]
    public async Task APeersReadReceiptArrivesWhenTheDevicesOwnRequestDrainsTheInbox()
    {
        var sender = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var recipient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var drive = TargetDrive.NewTargetDrive();
        await PrepareScenario(sender, recipient, drive);

        try
        {
            // Into the sender's inbox before any of its sockets is connected, so only the request below drains it
            var (file, copy) = await SendFileAsync(sender, recipient, drive);
            await SendReadReceiptAsync(recipient, copy);
            await WaitForInboxItemAsync(sender, drive);

            var socket = await ConnectAsync(sender, sender.GetTokenContext(), drive, excludeOwnWrites: true);
            try
            {
                // Drained by a request made with the socket's own token; the receipt is the recipient's write
                var drain = await sender.DriveRedux.ProcessInbox(drive);
                Assert.That(drain.IsSuccessStatusCode, Is.True, $"process inbox failed: {drain.StatusCode}");

                Assert.That(await NextEventsAsync(socket, ClientNotificationType.FileModified), Is.EqualTo($"fileModified {file.FileId}"));
            }
            finally
            {
                await socket.DisconnectAsync();
            }
        }
        finally
        {
            await _scaffold.OldOwnerApi.DisconnectIdentities(sender.Identity.OdinId, recipient.Identity.OdinId);
        }
    }

    [Test]
    public async Task APeersReadReceiptArrivesWhenTheServerDrainsTheInboxForTheSocket()
    {
        var sender = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var recipient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var drive = TargetDrive.NewTargetDrive();
        await PrepareScenario(sender, recipient, drive);

        // Connected once the upload's outbox is done, so the receipt is the only write left to arrive
        var (file, copy) = await SendFileAsync(sender, recipient, drive);
        var socket = await ConnectAsync(sender, sender.GetTokenContext(), drive, excludeOwnWrites: true);
        try
        {
            // No ProcessInbox: when the receipt arrives the server drains the inbox with a connected socket's context
            await SendReadReceiptAsync(recipient, copy);

            Assert.That(await NextEventsAsync(socket, ClientNotificationType.FileModified), Is.EqualTo($"fileModified {file.FileId}"));
        }
        finally
        {
            await socket.DisconnectAsync();
            await _scaffold.OldOwnerApi.DisconnectIdentities(sender.Identity.OdinId, recipient.Identity.OdinId);
        }
    }

    // ---------------------------------------------------------------------------------------------

    private static Task<OwnerDriveEventSocket> ConnectAsync(OwnerApiClientRedux owner, OwnerAuthTokenContext token,
        TargetDrive drive, bool excludeOwnWrites) =>
        OwnerDriveEventSocket.ConnectAsync(owner.Identity.OdinId, token, new EstablishConnectionOptions
        {
            Drives = [drive], ResultShape = FileResultShape.Minimal, ExcludeOwnWrites = excludeOwnWrites
        });

    /// <summary>
    /// The socket's next events, each as "type fileId" (the fileRef's, or for a hard delete the previousFileRef's)
    /// </summary>
    private static async Task<string> NextEventsAsync(OwnerDriveEventSocket socket, params ClientNotificationType[] expected)
    {
        var events = new List<string>();
        foreach (var type in expected)
        {
            var data = await socket.NextAsync(type);
            var fileRef = data.TryGetProperty("fileRef", out var r) ? r : data.GetProperty("previousFileRef");
            events.Add($"{JsonNamingPolicy.CamelCase.ConvertName(type.ToString())} {fileRef.GetProperty("fileId").GetGuid()}");
        }

        return string.Join(", ", events);
    }

    private static async Task<UploadResult> UploadAsync(UniversalDriveApiClient client, TargetDrive drive)
    {
        var response = await client.UploadNewMetadata(drive, SampleMetadataData.Create(fileType: 100));
        Assert.That(response.IsSuccessStatusCode, Is.True, $"upload failed: {response.StatusCode}");
        return response.Content!;
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
    /// Sends a file from <paramref name="sender"/> to <paramref name="recipient"/>, waiting until it is delivered;
    /// the sender's file and the recipient's copy
    /// </summary>
    private static async Task<(ExternalFileIdentifier file, ExternalFileIdentifier copy)> SendFileAsync(
        OwnerApiClientRedux sender, OwnerApiClientRedux recipient, TargetDrive drive)
    {
        var metadata = SampleMetadataData.Create(fileType: 100);
        metadata.AllowDistribution = true;
        var upload = await sender.DriveRedux.UploadNewMetadata(drive, metadata,
            new TransitOptions { Recipients = [recipient.Identity.OdinId] });
        Assert.That(upload.IsSuccessStatusCode, Is.True, $"upload failed: {upload.StatusCode}");
        await sender.DriveRedux.WaitForEmptyOutbox(drive);

        var copy = await recipient.DriveRedux.QueryByGlobalTransitId(upload.Content!.GlobalTransitIdFileIdentifier);
        var copyId = copy.Content?.SearchResults.SingleOrDefault()?.FileId;
        Assert.That(copyId, Is.Not.Null, $"the recipient has no copy: {copy.StatusCode}");

        return (upload.Content.File, new ExternalFileIdentifier { FileId = copyId!.Value, TargetDrive = drive });
    }

    /// <summary>
    /// The recipient marks its copy read; the receipt goes to the sender's inbox (a receipt is never written directly)
    /// </summary>
    private static async Task SendReadReceiptAsync(OwnerApiClientRedux recipient, ExternalFileIdentifier copy)
    {
        var receipt = await recipient.DriveRedux.SendReadReceipt([copy]);
        Assert.That(receipt.IsSuccessStatusCode, Is.True, $"read receipt failed: {receipt.StatusCode}");
    }

    private static async Task WaitForInboxItemAsync(OwnerApiClientRedux owner, TargetDrive drive)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            var status = await owner.DriveRedux.GetDriveStatus(drive);
            if (status.Content?.Inbox.TotalItems > 0)
            {
                return;
            }

            Assert.That(DateTime.UtcNow, Is.LessThan(deadline), $"nothing arrived in {owner.Identity.OdinId}'s inbox: {status.StatusCode}");
            await Task.Delay(100);
        }
    }

    /// <summary>
    /// The drive on both, and the two connected, each in a circle that may write to it on the other (the sender sends
    /// files, the recipient read receipts)
    /// </summary>
    private static async Task PrepareScenario(OwnerApiClientRedux sender, OwnerApiClientRedux recipient, TargetDrive drive)
    {
        var circles = new Dictionary<OwnerApiClientRedux, Guid>();
        foreach (var owner in new[] { recipient, sender })
        {
            var created = await owner.DriveManager.CreateDrive(drive, "Exclude own writes peer drive", "", allowAnonymousReads: false);
            Assert.That(created.IsSuccessStatusCode, Is.True, $"create drive failed: {created.StatusCode}");

            circles[owner] = Guid.NewGuid();
            var circle = await owner.Network.CreateCircle(circles[owner], "Circle with drive access", new PermissionSetGrantRequest
            {
                Drives = [new DriveGrantRequest { PermissionedDrive = new PermissionedDrive { Drive = drive, Permission = DrivePermission.Write } }]
            });
            Assert.That(circle.IsSuccessStatusCode, Is.True, $"create circle failed: {circle.StatusCode}");
        }

        await sender.Connections.SendConnectionRequest(recipient.Identity.OdinId, [circles[sender]]);
        await recipient.Connections.AcceptConnectionRequest(sender.Identity.OdinId, [circles[recipient]]);
    }
}
