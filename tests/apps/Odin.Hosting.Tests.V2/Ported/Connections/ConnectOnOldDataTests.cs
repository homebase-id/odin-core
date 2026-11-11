#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Apps.Builtin;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Configuration.VersionUpgrade;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Util;
using Refit;
using static Odin.Hosting.Tests.V2.Ported.Connections.Introductions.IntroductionTestUtils;

namespace Odin.Hosting.Tests.V2.Ported.Connections;

/// <summary>
/// #2105: an identity whose data is below the release's data version must not make connections, as the
/// recipient or as the sender.
/// </summary>
/// <remarks>
/// <para>
/// Before v17 -&gt; v18 no stored circle is <see cref="CircleGrantOn.Connect"/>, and the system circles that
/// used to carry chat access are no longer granted, so an accept on that data made a connection with no
/// circle grants at all: every chat write from the new contact was refused with a 403.  The recipient now
/// refuses with <see cref="OdinClientErrorCode.RecipientRequiresUpgrade"/> until it has upgraded.
/// </para>
/// <para>
/// v12 is what the identity in the report was on.  <see cref="MakeDataOldAsync"/> also stores the Connect
/// circles as v12 data has them; the rest of the data is today's shape, so this pins the gate, not that
/// each migration converts real v12 data (the per-version tests cover that).
/// </para>
/// </remarks>
[TestFixture]
public class ConnectOnOldDataTests : V2Fixture
{
    private const int OldDataVersion = 12;

    /// <summary>The introduction test drives requests that are expected to fail delivery.</summary>
    protected override IReadOnlyCollection<string> ToleratedErrorLogSubstrings =>
        [OutboxDeliveryFailureLogged];

    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam, Identities.Merry];

    public enum CallerKind
    {
        Owner,
        App
    }

    public static IEnumerable<object[]> AutoConnectCallers()
    {
        yield return [CallerKind.Owner];
        yield return [CallerKind.App];
    }

    // a) ---------------------------------------------------------------------------------------

    [Test, TestCaseSource(nameof(AutoConnectCallers))]
    public async Task AnAppInitiatedRequest_ToARecipientOnOldData_IsRefusedAndNothingIsMade(CallerKind kind)
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await MakeDataOldAsync(sam);

        var response = await AutoConnectAsync(frodo, kind, sam.Identity);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"actual {response.StatusCode}");
        Assert.That(response.Content!.Outcome, Is.EqualTo(AutoConnectOutcome.RecipientRequiresUpgrade),
            $"detail: {response.Content.Detail}");

        await AssertNotConnectedAsync(sam, frodo.Identity);
        await AssertNotConnectedAsync(frodo, sam.Identity);

        var pending = await sam.Connections.GetIncomingRequestFrom(frodo.Identity);
        Assert.That(pending.Content, Is.Null, "the refused request must not be stored on the recipient");
    }

    [Test]
    public async Task AnOwnerRequest_ToARecipientOnOldData_IsRefusedWith5009()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await MakeDataOldAsync(sam);

        var send = await frodo.Connections.SendConnectionRequest(sam.Identity);

        Assert.That(send.IsSuccessStatusCode, Is.False, "the send should be refused");
        Assert.That(send.ErrorCode(), Is.EqualTo(OdinClientErrorCode.RecipientRequiresUpgrade), send.Error?.Content);

        var pending = await sam.Connections.GetIncomingRequestFrom(frodo.Identity);
        Assert.That(pending.Content, Is.Null, "the refused request must not be stored on the recipient");
    }

    // b) ---------------------------------------------------------------------------------------

    /// <summary>The request arrived while sam was current; sam's data is then older than the release.</summary>
    [Test]
    public async Task AManualAccept_OnOldData_IsRefused()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await SendPendingRequestAsync(frodo, sam);
        await MakeDataOldAsync(sam);

        var accept = await sam.Connections.AcceptIncomingRequestV2Async(frodo.Identity, null);

        Assert.That(accept.IsSuccessStatusCode, Is.False, "accept-incoming should be refused");
        Assert.That(accept.ErrorCode(), Is.EqualTo(OdinClientErrorCode.RecipientRequiresUpgrade), accept.Error?.Content);
        await AssertNotConnectedAsync(sam, frodo.Identity);
        await AssertNotConnectedAsync(frodo, sam.Identity);
    }

    [Test]
    public async Task SendBecomesAccept_OnOldData_IsRefused()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await SendPendingRequestAsync(frodo, sam);
        await MakeDataOldAsync(sam);

        // A request from frodo is waiting, so sam's send is an accept of it.  Sam is the caller, so it hears
        // the refusal as its own upgrade.
        var send = await sam.Connections.SendConnectionRequest(frodo.Identity);

        Assert.That(send.IsSuccessStatusCode, Is.False, "send-becomes-accept should be refused");
        Assert.That(send.ErrorCode(), Is.EqualTo(OdinClientErrorCode.SenderRequiresUpgrade), send.Error?.Content);
        await AssertNotConnectedAsync(sam, frodo.Identity);
        await AssertNotConnectedAsync(frodo, sam.Identity);
    }

    /// <summary>
    /// Frodo introduces sam and merry.  Sam's introductory request reaches merry while merry is current;
    /// merry's data is then older than the release, and the forced auto-accept must not connect them.
    /// </summary>
    /// <remarks>
    /// Merry's outbox is not drained: merry's own introductory request would make the connection from the
    /// other side, where sam is the one accepting.
    /// </remarks>
    [Test]
    public async Task AnIntroductionAutoAccept_OnOldData_IsRefused()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var merry = await LoginAsOwner(Identities.Merry);

        await sam.Admin.DisableAutoAcceptIntroductions();
        await merry.Admin.DisableAutoAcceptIntroductions();
        await PrepareIntroducerAndClearIntroductionsAsync(frodo, sam, merry);

        var intro = await Requests(frodo).SendIntroductions(new IntroductionGroup
        {
            Message = "frodo introduces sam and merry",
            Recipients = [sam.Identity, merry.Identity]
        });
        Assert.That(intro.IsSuccessStatusCode, Is.True, $"introduction failed: {intro.StatusCode}");
        await frodo.Sync.DrainOutboxAsync();
        var processed = await Requests(sam).ProcessIncomingIntroductions();
        Assert.That(processed.IsSuccessStatusCode, Is.True, $"sam could not process introductions: {processed.StatusCode}");
        await sam.Sync.DrainOutboxAsync();

        var fromSam = await merry.Connections.GetIncomingRequestFrom(sam.Identity);
        Assert.That(fromSam.Content, Is.Not.Null, "precondition: sam's introductory request reached merry");

        await MakeDataOldAsync(merry);

        var force = await Requests(merry).AutoAcceptEligibleIntroductions();
        Assert.That(force.IsSuccessStatusCode, Is.True, $"auto-accept endpoint failed: {force.StatusCode}");

        await AssertNotConnectedAsync(merry, sam.Identity);
        await AssertNotConnectedAsync(sam, merry.Identity);
    }

    // c) ---------------------------------------------------------------------------------------

    [Test]
    public async Task AfterTheUpgrade_TheSameRequestAutoAccepts_AndChatIsDelivered()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await MakeDataOldAsync(sam);

        var refused = await AutoConnectAsync(frodo, CallerKind.Owner, sam.Identity);
        Assert.That(refused.Content!.Outcome, Is.EqualTo(AutoConnectOutcome.RecipientRequiresUpgrade),
            $"precondition: refused before the upgrade; detail: {refused.Content.Detail}");

        await RunUpgradeAsync(sam);

        var response = await AutoConnectAsync(frodo, CallerKind.Owner, sam.Identity);
        Assert.That(response.Content!.Outcome, Is.EqualTo(AutoConnectOutcome.Connected), $"detail: {response.Content.Detail}");

        Assert.That((await GetIcrAsync(sam, frodo.Identity)).PeerKeyStore.CircleGrants.Keys,
            Does.Contain(BuiltinCircles.ChatCircle.Id.Value), "frodo should be in sam's Chat circle");
        Assert.That(await SendChatFileAsync(frodo, sam), Is.True, "frodo's chat file should reach sam");
    }

    // d) ---------------------------------------------------------------------------------------

    /// <summary>
    /// The connection as production has it: made on old data, so the recipient granted nothing.  Running
    /// the upgrade puts the sender in Chat (v17 -&gt; v18 enrols every connected identity) and chat works.
    /// </summary>
    /// <remarks>
    /// The gate now prevents making such a connection, so it is made on current data and then stripped of
    /// the grant the old accept never gave.
    /// </remarks>
    [Test]
    public async Task AConnectionMadeOnOldData_IsRepairedByTheUpgrade()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        var response = await AutoConnectAsync(frodo, CallerKind.Owner, sam.Identity);
        Assert.That(response.Content!.Outcome, Is.EqualTo(AutoConnectOutcome.Connected), $"detail: {response.Content.Detail}");

        var (scope, ctx) = await MigrationContextAsync(sam);
        await scope.Resolve<CircleNetworkService>().RevokeCircleAccessAsync(BuiltinCircles.ChatCircle.Id, frodo.Identity, ctx);

        var stripped = await GetIcrAsync(sam, frodo.Identity);
        Assert.That(stripped.IsConnected(), Is.True);
        Assert.That(stripped.PeerKeyStore.CircleGrants.Keys, Is.Empty,
            "precondition: no circle grants, as the accept on old data left it");
        Assert.That(await SendChatFileAsync(frodo, sam), Is.False, "precondition: frodo's chat is refused");

        await MakeDataOldAsync(sam);
        await RunUpgradeAsync(sam);

        Assert.That((await GetIcrAsync(sam, frodo.Identity)).PeerKeyStore.CircleGrants.Keys,
            Does.Contain(BuiltinCircles.ChatCircle.Id.Value), "the upgrade should put frodo in sam's Chat circle");
        Assert.That(await SendChatFileAsync(frodo, sam), Is.True, "frodo's chat file should reach sam after the upgrade");
    }

    // Sender side -----------------------------------------------------------------------------

    /// <summary>
    /// The grant the recipient holds is minted from the sender's Connect circles.  On old data there are
    /// none, so the sender's messages arrive while every reply is refused; the sender refuses to connect.
    /// </summary>
    [Test, TestCaseSource(nameof(AutoConnectCallers))]
    public async Task AnAutoConnect_FromASenderOnOldData_IsRefusedAndNothingIsMade(CallerKind kind)
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await MakeDataOldAsync(frodo);

        var response = await AutoConnectAsync(frodo, kind, sam.Identity);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"actual {response.StatusCode}");
        Assert.That(response.Content!.Outcome, Is.EqualTo(AutoConnectOutcome.SenderRequiresUpgrade),
            $"detail: {response.Content.Detail}");

        await AssertNotConnectedAsync(frodo, sam.Identity);
        await AssertNotConnectedAsync(sam, frodo.Identity);
        Assert.That((await frodo.Connections.GetOutgoingSentRequestTo(sam.Identity)).Content, Is.Null,
            "nothing should be stored on the sender");
        Assert.That((await sam.Connections.GetIncomingRequestFrom(frodo.Identity)).Content, Is.Null,
            "nothing should reach the recipient");
    }

    [Test]
    public async Task AnOwnerRequest_FromASenderOnOldData_IsRefusedWith5011()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await MakeDataOldAsync(frodo);

        var send = await frodo.Connections.SendConnectionRequest(sam.Identity);

        Assert.That(send.IsSuccessStatusCode, Is.False, "the send should be refused");
        Assert.That(send.ErrorCode(), Is.EqualTo(OdinClientErrorCode.SenderRequiresUpgrade), send.Error?.Content);
        Assert.That((await sam.Connections.GetIncomingRequestFrom(frodo.Identity)).Content, Is.Null,
            "nothing should reach the recipient");
    }

    [Test]
    public async Task AfterTheSendersUpgrade_ChatWorksBothWays()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await MakeDataOldAsync(frodo);

        var refused = await AutoConnectAsync(frodo, CallerKind.Owner, sam.Identity);
        Assert.That(refused.Content!.Outcome, Is.EqualTo(AutoConnectOutcome.SenderRequiresUpgrade),
            $"precondition: refused before the upgrade; detail: {refused.Content.Detail}");

        await RunUpgradeAsync(frodo);

        var response = await AutoConnectAsync(frodo, CallerKind.Owner, sam.Identity);
        Assert.That(response.Content!.Outcome, Is.EqualTo(AutoConnectOutcome.Connected), $"detail: {response.Content.Detail}");

        Assert.That((await GetIcrAsync(frodo, sam.Identity)).PeerKeyStore.CircleGrants.Keys,
            Does.Contain(BuiltinCircles.ChatCircle.Id.Value), "sam should be in frodo's Chat circle");
        Assert.That(await SendChatFileAsync(frodo, sam), Is.True, "frodo's chat file should reach sam");
        Assert.That(await SendChatFileAsync(sam, frodo), Is.True, "sam's reply should reach frodo");
    }

    // -----------------------------------------------------------------------------------------
    // helpers
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Puts the owner's data in the shape the report's identity had: below the release's version, and with
    /// no stored circle declaring <see cref="CircleGrantOn.Connect"/> yet -- only v17 -&gt; v18 writes that.
    /// </summary>
    private async Task MakeDataOldAsync(OwnerSession owner)
    {
        var scope = Host.GetTenantScope(owner.Identity.DomainName);
        var definitions = scope.Resolve<CircleDefinitionService>();
        foreach (var def in BuiltinApps.AllCircles.Where(c => c.GrantOn == CircleGrantOn.Connect))
        {
            await definitions.ApplyTreeDefinitionAsync(def.Id, def.AppId!.Value, CircleGrantOn.None, def.Designation);
        }

        await scope.Resolve<TenantConfigService>().ForceVersionNumberAsync(OldDataVersion);
    }

    /// <summary>The upgrade job the scheduler runs, as <c>SystemCircleRemovalMigrationTests</c> runs it.</summary>
    private async Task RunUpgradeAsync(OwnerSession owner)
    {
        var (scope, _) = await MigrationContextAsync(owner);
        var (iv, encryptedToken) = AesCbc.Encrypt(owner.Token.ToPortableBytes(),
            scope.Resolve<TenantContext>().TemporalEncryptionKey);
        await scope.Resolve<VersionUpgradeService>().UpgradeAsync(new VersionUpgradeJobData
        {
            Tenant = owner.Identity,
            Iv = iv,
            EncryptedToken = encryptedToken
        }, CancellationToken.None);

        var config = scope.Resolve<TenantConfigService>();
        var failure = await config.GetVersionFailureInfoAsync();
        Assert.That(failure, Is.Null, $"the upgrade failed at v{failure?.FailedDataVersionNumber}; see the log");
        Assert.That((await config.GetVersionInfoAsync()).DataVersionNumber, Is.EqualTo(Odin.Services.Version.DataVersionNumber));
    }

    /// <summary>An owner-origin request, which the recipient does not auto-accept: it waits as pending.</summary>
    private static async Task SendPendingRequestAsync(OwnerSession sender, OwnerSession recipient)
    {
        var send = await sender.Connections.SendConnectionRequest(recipient.Identity);
        Assert.That(send.IsSuccessStatusCode, Is.True, $"send failed: {send.StatusCode}");
        var pending = await recipient.Connections.GetIncomingRequestFrom(sender.Identity);
        Assert.That(pending.Content, Is.Not.Null, "precondition: the request is pending on the recipient");
    }

    private static async Task<ApiResponse<ConnectionRequestResult>> AutoConnectAsync(
        OwnerSession sender, CallerKind kind, OdinId recipient)
    {
        var header = new ConnectionRequestHeader
        {
            Recipient = recipient,
            Message = "auto-connect",
            ContactData = new ContactRequestData { Name = "test" },
            CircleIds = new List<GuidId>()
        };

        if (kind == CallerKind.Owner)
        {
            return await sender.Connections.AutoConnectAsync(header);
        }

        var drive = TargetDrive.NewTargetDrive();
        await sender.Admin.CreateDrive(drive, "auto-connect app drive");
        var app = await AppSession.SetupAsync(sender, drive, DrivePermission.Read, PermissionKeyAllowance.Apps);
        return await new V2ConnectionRequestsClient(app.Identity, app.Factory).AutoConnectAsync(header);
    }

    /// <summary>Sends a file on the chat drive from sender to recipient; true when the recipient has it.</summary>
    private static async Task<bool> SendChatFileAsync(OwnerSession sender, OwnerSession recipient)
    {
        var metadata = SampleMetadataData.Create(fileType: 2105, acl: AccessControlList.Connected);
        metadata.AllowDistribution = true;

        var upload = await sender.Drives.Writer.UploadNewMetadata(
            WellKnownAppDrives.ChatDrive.Alias,
            metadata,
            transitOptions: new TransitOptions { Recipients = [recipient.Identity.DomainName] });
        Assert.That(upload.IsSuccessStatusCode, Is.True, $"upload to the sender's own chat drive failed: {upload.StatusCode}");
        var globalTransitId = upload.Content!.GlobalTransitId!.Value;

        await sender.Sync.DrainOutboxAsync();
        await recipient.Sync.ProcessInboxAsync(WellKnownAppDrives.ChatDrive);

        var query = await recipient.Drives.Reader.GetBatchAsync(WellKnownAppDrives.ChatDrive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { GlobalTransitId = [globalTransitId] },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 10, IncludeMetadataHeader = true }
        });
        Assert.That(query.IsSuccessStatusCode, Is.True, $"recipient query failed: {query.StatusCode}");
        return query.Content!.SearchResults.Any();
    }

    private async Task<IdentityConnectionRegistration> GetIcrAsync(OwnerSession owner, OdinId target)
    {
        var icr = await Host.GetTenantScope(owner.Identity.DomainName).Resolve<CircleNetworkStorage>().GetAsync(target);
        Assert.That(icr, Is.Not.Null, $"no connection record for {target}");
        return icr!;
    }

    private async Task AssertNotConnectedAsync(OwnerSession owner, OdinId target)
    {
        var icr = await Host.GetTenantScope(owner.Identity.DomainName).Resolve<CircleNetworkStorage>().GetAsync(target);
        Assert.That(icr?.IsConnected() ?? false, Is.False, $"{owner.Identity} should not be connected to {target}");
    }
}
