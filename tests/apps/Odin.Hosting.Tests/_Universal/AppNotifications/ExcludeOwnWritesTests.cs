using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Odin.Test.Helpers;
using Odin.Hosting.Tests._Universal.ApiClient;
using Odin.Hosting.Tests._Universal.ApiClient.App;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Contacts;
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
            // A: add, modify, soft delete (V1), hard delete (V2); then B, A, B, A add one each, so each socket's own
            // writes come before one of the other's, which proves they were left out rather than not yet read
            var f1 = await UploadAsync(owner.DriveRedux, drive);
            var update = await owner.DriveRedux.UpdateExistingMetadata(f1.File, f1.NewVersionTag, SampleMetadataData.Create(fileType: 100));
            Assert.That(update.IsSuccessStatusCode, Is.True, $"update failed: {update.StatusCode}");
            var softDelete = await owner.DriveRedux.SoftDeleteFile(f1.File);
            Assert.That(softDelete.IsSuccessStatusCode, Is.True, $"soft delete failed: {softDelete.StatusCode}");
            var tombstone = await owner.DriveRedux.GetFileHeader(f1.File);
            await owner.HardDeleteOneV2Async(drive, f1.File.FileId, tombstone.Content!.FileMetadata.VersionTag);
            var f2 = await UploadAsync(driveB, drive);
            var f3 = await UploadAsync(owner.DriveRedux, drive);
            var f4 = await UploadAsync(driveB, drive);
            var f5 = await UploadAsync(owner.DriveRedux, drive);

            var fromA = $"fileAdded {f1.File.FileId}, fileModified {f1.File.FileId}, fileDeleted {f1.File.FileId}, fileDeleted {f1.File.FileId}";
            Assert.That(await NextEventsAsync(aExcluding, 2),
                Is.EqualTo($"fileAdded {f2.File.FileId}, fileAdded {f4.File.FileId}"), "A gets B's writes and none of its own");
            Assert.That(await NextEventsAsync(bExcluding, 6),
                Is.EqualTo($"{fromA}, fileAdded {f3.File.FileId}, fileAdded {f5.File.FileId}"), "B gets A's writes and none of its own");
            Assert.That(await NextEventsAsync(aAll, 8),
                Is.EqualTo($"{fromA}, fileAdded {f2.File.FileId}, fileAdded {f3.File.FileId}, fileAdded {f4.File.FileId}, fileAdded {f5.File.FileId}"),
                "without the option nothing is left out");
        }
        finally
        {
            await aExcluding.DisconnectAsync();
            await bExcluding.DisconnectAsync();
            await aAll.DisconnectAsync();
        }
    }

    [Test]
    public async Task ASoftDeleteEventCarriesTheTombstonesModifiedTime()
    {
        // Clearing the reaction summary moves modified after the tombstone is written. A client that leaves out its
        // own writes compares the other's events with the feed, so the event must carry that final time too.
        var owner = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var drive = TargetDrive.NewTargetDrive();
        await owner.DriveManager.CreateDrive(drive, "Soft delete event time test drive", "", allowAnonymousReads: false);
        var socket = await ConnectAsync(owner, owner.GetTokenContext(), drive, excludeOwnWrites: false);
        try
        {
            var file = await UploadAsync(owner.DriveRedux, drive);
            await socket.NextAsync(ClientNotificationType.FileAdded);
            var softDelete = await owner.DriveRedux.SoftDeleteFile(file.File);
            Assert.That(softDelete.IsSuccessStatusCode, Is.True, $"soft delete failed: {softDelete.StatusCode}");

            var deleted = await socket.NextAsync(ClientNotificationType.FileDeleted);
            var tombstone = await owner.DriveRedux.GetFileHeader(file.File);
            Assert.That(deleted.GetProperty("fileRef").GetProperty("updated").GetInt64(),
                Is.EqualTo(tombstone.Content!.FileMetadata.Updated.milliseconds), deleted.ToString());
        }
        finally
        {
            await socket.DisconnectAsync();
        }
    }

    [Test]
    public async Task AnOwnWriteMadeWithRaisedPermissionsIsStillLeftOut()
    {
        // A contact is written with a copy of the request's context that bypasses the drive ACL
        // (OdinContextUpgrades.UpgradeToByPassAclCheck): the same request with more permissions, so still its own write
        var owner = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var tokenA = owner.GetTokenContext();
        var tokenB = await _scaffold.OldOwnerApi.LoginAnotherOwnerSessionAsync(owner.Identity.OdinId);
        var contactsA = new V1ContactsClient(owner.Identity.OdinId, new RootOwnerApiClientFactory(tokenA));
        var contactsB = new V1ContactsClient(owner.Identity.OdinId, new RootOwnerApiClientFactory(tokenB));

        var aExcluding = await ConnectAsync(owner, tokenA, WellKnownAppDrives.ContactDrive, excludeOwnWrites: true);
        try
        {
            await CreateContactAsync(contactsA, TestIdentities.Pippin.OdinId);
            var theirs = await CreateContactAsync(contactsB, TestIdentities.Merry.OdinId);

            var added = await aExcluding.NextAsync(ClientNotificationType.FileAdded);
            Assert.That(added.GetProperty("fileRef").GetProperty("uniqueId").GetGuid(), Is.EqualTo(theirs),
                $"A's own contact should have been left out; got {added}");
        }
        finally
        {
            await aExcluding.DisconnectAsync();
        }
    }

    [Test]
    public async Task TwoClientsOfOneAppEachGetTheOthersWritesButNotTheirOwn()
    {
        // Two clients of one app, each registered on its own (as each filesync computer is): each has a token of its own
        var owner = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var drive = TargetDrive.NewTargetDrive();
        var appId = Guid.NewGuid();
        var app = await _scaffold.OldOwnerApi.SetupTestSampleApp(appId, TestIdentities.Samwise, targetDrive: drive);
        var (tokenB, secretB) = await owner.AppManager.RegisterAppClient(appId);

        var clientA = new AppApiClientRedux(owner.Identity.OdinId, app.ClientAuthenticationToken, app.SharedSecret);
        var clientB = new AppApiClientRedux(owner.Identity.OdinId, tokenB, secretB);
        var socketA = await ConnectAppAsync(owner, app.ClientAuthenticationToken, app.SharedSecret, drive);
        var socketB = await ConnectAppAsync(owner, tokenB, secretB, drive);

        try
        {
            var f1 = await UploadAsync(clientA.DriveRedux, drive);
            var f2 = await UploadAsync(clientB.DriveRedux, drive);
            var f3 = await UploadAsync(clientA.DriveRedux, drive);
            var f4 = await UploadAsync(clientB.DriveRedux, drive);
            var f5 = await UploadAsync(clientA.DriveRedux, drive);

            // Each client's own writes come before one of the other's, so they were left out, not just not yet read
            Assert.That(await NextEventsAsync(socketA, 2),
                Is.EqualTo($"fileAdded {f2.File.FileId}, fileAdded {f4.File.FileId}"), "A gets B's writes only");
            Assert.That(await NextEventsAsync(socketB, 3),
                Is.EqualTo($"fileAdded {f1.File.FileId}, fileAdded {f3.File.FileId}, fileAdded {f5.File.FileId}"), "B gets A's writes only");
        }
        finally
        {
            await socketA.DisconnectAsync();
            await socketB.DisconnectAsync();
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

                Assert.That(await NextEventsAsync(socket, 1), Is.EqualTo($"fileModified {file.FileId}"));
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

        DriveEventSocket socket = null;
        try
        {
            // Connected once the upload's outbox is done, so the receipt is the only write left to arrive
            var (file, copy) = await SendFileAsync(sender, recipient, drive);
            socket = await ConnectAsync(sender, sender.GetTokenContext(), drive, excludeOwnWrites: true);

            // No ProcessInbox: when the receipt arrives the server drains the inbox with a connected socket's context
            await SendReadReceiptAsync(recipient, copy);

            Assert.That(await NextEventsAsync(socket, 1), Is.EqualTo($"fileModified {file.FileId}"));
        }
        finally
        {
            await (socket?.DisconnectAsync() ?? Task.CompletedTask);
            await _scaffold.OldOwnerApi.DisconnectIdentities(sender.Identity.OdinId, recipient.Identity.OdinId);
        }
    }

    // ---------------------------------------------------------------------------------------------

    private static Task<DriveEventSocket> ConnectAppAsync(OwnerApiClientRedux owner, ClientAuthenticationToken token,
        byte[] sharedSecret, TargetDrive drive) =>
        DriveEventSocket.ConnectAppAsync(owner.Identity.OdinId, token, sharedSecret, new EstablishConnectionOptions
        {
            Drives = [drive], ResultShape = FileResultShape.Minimal, ExcludeOwnWrites = true
        });

    private static Task<DriveEventSocket> ConnectAsync(OwnerApiClientRedux owner, OwnerAuthTokenContext token,
        TargetDrive drive, bool excludeOwnWrites) =>
        DriveEventSocket.ConnectOwnerAsync(owner.Identity.OdinId, token, new EstablishConnectionOptions
        {
            Drives = [drive], ResultShape = FileResultShape.Minimal, ExcludeOwnWrites = excludeOwnWrites
        });

    /// <summary>
    /// The socket's next <paramref name="count"/> events, each as "type fileId" (the fileRef's, or for a hard delete
    /// the previousFileRef's)
    /// </summary>
    private static async Task<string> NextEventsAsync(DriveEventSocket socket, int count)
    {
        var events = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var (type, data) = await socket.NextAsync();
            var fileRef = data.TryGetProperty("fileRef", out var r) ? r : data.GetProperty("previousFileRef");
            events.Add($"{JsonNamingPolicy.CamelCase.ConvertName(type.ToString())} {fileRef.GetProperty("fileId").GetGuid()}");
        }

        return string.Join(", ", events);
    }

    /// <summary>
    /// Creates a contact for <paramref name="odinId"/>; its unique id
    /// </summary>
    private static async Task<Guid> CreateContactAsync(V1ContactsClient contacts, OdinId odinId)
    {
        var create = await contacts.OwnerCreateAsync(new CreateContactRequest
        {
            Content = new ContactContent { OdinId = odinId, Name = new ContactName { DisplayName = odinId.DomainName } }
        });
        Assert.That(create.IsSuccessStatusCode, Is.True, $"create contact failed: {create.StatusCode} {create.Error?.Content}");
        return create.Content!.UniqueId;
    }

    private static async Task<UploadResult> UploadAsync(UniversalDriveApiClient client, TargetDrive drive)
    {
        var response = await client.UploadNewMetadata(drive, SampleMetadataData.Create(fileType: 100));
        Assert.That(response.IsSuccessStatusCode, Is.True, $"upload failed: {response.StatusCode}");
        return response.Content!;
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

    private static Task WaitForInboxItemAsync(OwnerApiClientRedux owner, TargetDrive drive) =>
        Poll.UntilAsync(async () => (await owner.DriveRedux.GetDriveStatus(drive)).Content?.Inbox.TotalItems > 0,
            TimeSpan.FromSeconds(15), () => $"nothing arrived in {owner.Identity.OdinId}'s inbox");

    /// <summary>
    /// The drive on both, and the two connected, each in a circle that may write to it on the other (the sender sends
    /// files, the recipient read receipts)
    /// </summary>
    private async Task PrepareScenario(OwnerApiClientRedux sender, OwnerApiClientRedux recipient, TargetDrive drive)
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

        await _scaffold.OldOwnerApi.CreateConnection(sender.Identity.OdinId, recipient.Identity.OdinId, new CreateConnectionOptions
        {
            CircleIdsGrantedToRecipient = [circles[sender]],
            CircleIdsGrantedToSender = [circles[recipient]]
        });
    }

    /// <summary>
    /// An owner client rooted at the host, for clients whose routes carry the full path (V1ContactsClient);
    /// <see cref="OwnerApiClientFactory"/> roots at the owner API base path
    /// </summary>
    private sealed class RootOwnerApiClientFactory(OwnerAuthTokenContext token) : IApiClientFactory
    {
        private readonly OwnerApiClientFactory _inner = new(token.AuthenticationResult, token.SharedSecret.GetKey());

        public SensitiveByteArray SharedSecret => _inner.SharedSecret;

        public HttpClient CreateHttpClient(OdinId identity, out SensitiveByteArray sharedSecret,
            FileSystemType fileSystemType = FileSystemType.Standard)
        {
            var client = _inner.CreateHttpClient(identity, out sharedSecret, fileSystemType);
            client.BaseAddress = new Uri($"https://{identity}:{WebScaffold.HttpsPort}");
            return client;
        }
    }
}
