using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Hosting.Controllers;
using Odin.Hosting.Tests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.Connections;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;
using Refit;
using static Odin.Hosting.Tests.V2.Ported.Connections.ConnectionAsserts;

namespace Odin.Hosting.Tests.V2.Ported.Connections;

/// <summary>
/// Port of <c>OwnerApi/Membership/Connections/ConnectionRequestTests</c>. The connection-request
/// state matrix: for each combination of what the two identities believe about each other
/// (none / outgoing / incoming / connected), can they still reach a connected state — plus the two
/// auto-accept behaviours (an incoming request auto-approves when a matching outgoing request
/// exists, and is refused with 400 when it does not).
/// </summary>
/// <remarks>
/// Calls whose status code is the thing under test — sending a request, accepting one, deleting one
/// and asserting it is then a 404, disconnecting with an explicit <c>notifyRemote</c> — go through the
/// V1 Refit interfaces via <see cref="OwnerSession.RefitFor{T}"/>, which is what the original's
/// <c>OwnerApiClient.Network</c> helpers did underneath; <c>owner.Admin</c> (arrange-only, throws on
/// non-2xx) is the wrong tool for those. The plain reads (<c>GetConnectionInfo</c>,
/// <c>GetIncomingRequestFrom</c>, <c>GetOutgoingSentRequestTo</c>) go through
/// <c>owner.Connections</c>, the same spelling the sibling <see cref="CircleNetworkServiceAppTests"/>
/// uses. <c>DisableAutoAcceptIntroductions</c> is <c>owner.Admin.DisableAutoAcceptIntroductions()</c>,
/// which wraps the same endpoint and flag the original's configuration client used.
/// <para>
/// <c>DisconnectFrom</c> stays on Refit for a second reason: <c>owner.Connections.DisconnectFrom</c>
/// cannot express <c>notifyRemote</c>, and the two interfaces disagree on its default (the owner one
/// sends <c>true</c>, the <c>_Universal</c> one <c>false</c>) — several tests here depend on a
/// one-sided disconnect, and <c>Cleanup</c> depends on a notifying one.
/// </para>
/// <para>
/// Carried defects, behaviour left as found:
/// <list type="bullet">
/// <item>The original's <c>SendConnectionRequestRaw</c> put the <i>recipient's</i>
/// <c>ContactData</c> on the outgoing request header rather than the sender's. Reproduced here (see
/// <see cref="SendConnectionRequestRaw"/>); it is inert in this fixture, which never reads contact
/// data back.</item>
/// <item><see cref="CanConnectWhenState_Merry_Incoming_Pippin_Outgoing"/> is a body-less
/// <c>Assert.Pass("Already tested above")</c>.</item>
/// <item>Three tests carry <c>[Ignore]</c> from the original, verbatim.</item>
/// </list>
/// </para>
/// <para>
/// The <c>Cleanup</c> calls are kept rather than dropped as lifecycle: each one asserts (the delete
/// succeeded, the request is gone, the disconnect left status <c>None</c>), so they are tests.
/// <c>FailToConnect_Merry_Incoming_And_Pippin_Has_Deleted_Outgoing_Request</c> was the original's
/// <c>…_Sam_Incoming_And_Frodo_…</c>, whose comment explained the switch to a second identity pair as
/// a <c>WebScaffold</c> clean-up workaround; per-test reset removes that reason, so it runs on
/// Merry/Pippin like every other test here and the fixture boots two tenants instead of four. Three
/// stubs the original left commented out are <c>[Ignore]</c>d test methods here, so the runner lists
/// them. <c>SetupCallerWithOwner</c> ordering is not in play: no caller matrix, <c>LoginAsOwner</c>
/// only.
/// </para>
/// </remarks>
[TestFixture]
public class ConnectionRequestTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Merry, Identities.Pippin];

    [Test]
    [Description("Merry: None, Pippin: None")]
    public async Task CanConnectWhenState_Merry_NotConnected_Pippin_NotConnected()
    {
        var sender = await LoginAsOwner(Identities.Merry);
        var recipient = await LoginAsOwner(Identities.Pippin);

        await Connect(sender, recipient);
        await AssertConnected(sender, recipient);
        await Cleanup(sender, recipient);
    }

    [Test]
    [Description("Merry: Connected, Pippin: Connected")]
    [Ignore("Invalid due to invitations feature")]
    public async Task CanConnectWhenState_Merry_Connected_Pippin_Connected()
    {
        var sender = await LoginAsOwner(Identities.Merry);
        var recipient = await LoginAsOwner(Identities.Pippin);

        await Connect(sender, recipient);
        await AssertConnected(sender, recipient);

        //
        // try to connect a second time
        //

        await Connect(sender, recipient);
        await AssertConnected(sender, recipient);
        await Cleanup(sender, recipient);
    }

    [Test]
    [Description("Merry: Connected, Pippin: None")]
    public async Task CanConnectWhenState_Merry_Connected_Pippin_NotConnected()
    {
        var sender = await LoginAsOwner(Identities.Merry);
        var recipient = await LoginAsOwner(Identities.Pippin);

        await Connect(sender, recipient);
        await AssertConnected(sender, recipient);

        // Pippin should disconnect (note: this relies on not telling the remote server you are disconnecting)
        await DisconnectFrom(recipient, sender.Identity, notifyRemote: false);

        await Connect(sender, recipient);
        await AssertConnected(sender, recipient);
        await Cleanup(sender, recipient);
    }


    [Test]
    [Description("Merry: Connected, Pippin: Outgoing")]
    public async Task CanConnectWhenState_Merry_Connected_Pippin_Outgoing()
    {
        var merryClient = await LoginAsOwner(Identities.Merry);
        var pippinClient = await LoginAsOwner(Identities.Pippin);

        await Connect(merryClient, pippinClient);
        await AssertConnected(merryClient, pippinClient);

        // Pippin should disconnect (note: this relies on not telling the remote server you are disconnecting)
        await DisconnectFrom(pippinClient, merryClient.Identity, notifyRemote: false);
        await SendConnectionRequestTo(pippinClient, merryClient.Identity);

        // Merry deletes the incoming request
        await DeleteConnectionRequestFrom(merryClient, pippinClient.Identity);

        // They try to reconnect again fully
        await Connect(pippinClient, merryClient);
        await AssertConnected(pippinClient, merryClient);
        await Cleanup(pippinClient, merryClient);
    }

    [Test]
    [Ignore("Invalid due to invitations feature; we now accept the incoming request if you're sending one to the same recipient")]
    public async Task CanStoreSentRequestAndOutgoingRequestForSameIdentity()
    {
        // Scenario: merry sends request to pippin, therefore pippin has an incoming request from merry
        // pippin sends a request to merry, therefore pippin has an outgoing request to merry
        var merryClient = await LoginAsOwner(Identities.Merry);
        var pippinClient = await LoginAsOwner(Identities.Pippin);

        await SendConnectionRequestTo(merryClient, pippinClient.Identity);

        var pendingRequestFromMerry = await GetIncomingRequestFrom(pippinClient, merryClient.Identity);
        Assert.That(pendingRequestFromMerry, Is.Not.Null);
        Assert.That(pendingRequestFromMerry.SenderOdinId, Is.EqualTo(merryClient.Identity.DomainName));
        Assert.That(pendingRequestFromMerry.Direction, Is.EqualTo(ConnectionRequestDirection.Incoming));


        //Now that Pippin has an incoming request
        await SendConnectionRequestTo(pippinClient, merryClient.Identity);

        // Assert that we still have an outgoing request to merry and an incoming request from merry; two different requests
        var sentRequestToMerry = await GetOutgoingSentRequestTo(pippinClient, merryClient.Identity);
        Assert.That(sentRequestToMerry, Is.Not.Null);
        Assert.That(sentRequestToMerry.Recipient, Is.EqualTo(merryClient.Identity.DomainName));
        Assert.That(sentRequestToMerry.Direction, Is.EqualTo(ConnectionRequestDirection.Outgoing));

        var pendingRequestFromMerry2 = await GetIncomingRequestFrom(pippinClient, merryClient.Identity);
        Assert.That(pendingRequestFromMerry2, Is.Not.Null);
        Assert.That(pendingRequestFromMerry2.SenderOdinId, Is.EqualTo(merryClient.Identity.DomainName));
        Assert.That(pendingRequestFromMerry2.Direction, Is.EqualTo(ConnectionRequestDirection.Incoming));

        // They try to reconnect again fully
        await Connect(merryClient, pippinClient);
        await AssertConnected(merryClient, pippinClient);
        await Cleanup(merryClient, pippinClient);
    }

    [Test]
    [Description("Merry: Connected, Pippin: Incoming")]
    public async Task CanConnectWhenState_Merry__Connected_Pippin_Incoming()
    {
        var merryClient = await LoginAsOwner(Identities.Merry);
        var pippinClient = await LoginAsOwner(Identities.Pippin);

        await Connect(merryClient, pippinClient);
        await AssertConnected(merryClient, pippinClient);

        // Pippin should disconnect (note: this relies on not telling the remote server you are disconnecting)
        await DisconnectFrom(pippinClient, merryClient.Identity, notifyRemote: false);

        await SendConnectionRequestTo(merryClient, pippinClient.Identity);
        await DeleteSentRequestTo(merryClient, pippinClient.Identity);

        //Now that Pippin has an incoming request

        // They try to reconnect again fully
        await Connect(merryClient, pippinClient);
        await AssertConnected(merryClient, pippinClient);
        await Cleanup(merryClient, pippinClient);
    }

    [Test]
    [Description("Merry: Outgoing, Pippin: Connected")]
    public async Task CanConnectWhenState_Merry_Outgoing_Pippin_Connected()
    {
        var merryClient = await LoginAsOwner(Identities.Merry);
        var pippinClient = await LoginAsOwner(Identities.Pippin);

        //

        await Connect(merryClient, pippinClient);
        await AssertConnected(merryClient, pippinClient);

        // Merry disconnects one-sided so Pippin's side is still Connected (asserted below).
        await DisconnectFrom(merryClient, pippinClient.Identity, notifyRemote: false);
        await SendConnectionRequestTo(merryClient, pippinClient.Identity);

        await DeleteConnectionRequestFrom(pippinClient, merryClient.Identity);

        //
        // Assert state is ready for test
        //

        var merryInfo = await GetConnectionInfo(pippinClient, merryClient.Identity);
        Assert.That(merryInfo.Status, Is.EqualTo(ConnectionStatus.Connected));

        var pippinInfo = await GetOutgoingSentRequestTo(merryClient, pippinClient.Identity);
        Assert.That(pippinInfo, Is.Not.Null);

        //
        // They try to reconnect again fully
        //
        await Connect(merryClient, pippinClient);
        await AssertConnected(merryClient, pippinClient);
        await Cleanup(merryClient, pippinClient);
    }

    [Test]
    [Description("Merry: Outgoing, Pippin: Outgoing")]
    [Ignore("Invalid due to invitations feature; we now auto-approve requests when sending a request to a recipient who already sent you a request")]
    public async Task CanConnectWhenState_Merry_Outgoing_Pippin_Outgoing()
    {
        var merryClient = await LoginAsOwner(Identities.Merry);
        var pippinClient = await LoginAsOwner(Identities.Pippin);

        await SendConnectionRequestTo(merryClient, pippinClient.Identity);
        await SendConnectionRequestTo(pippinClient, merryClient.Identity);

        await DeleteConnectionRequestFrom(merryClient, pippinClient.Identity);
        await DeleteConnectionRequestFrom(pippinClient, merryClient.Identity);

        //
        // Assert state is ready for test
        //

        var merryInfo = await GetOutgoingSentRequestTo(pippinClient, merryClient.Identity);
        Assert.That(merryInfo, Is.Not.Null);

        var pippinInfo = await GetOutgoingSentRequestTo(merryClient, pippinClient.Identity);
        Assert.That(pippinInfo, Is.Not.Null);

        //
        // They try to reconnect again fully
        //
        await Connect(merryClient, pippinClient);
        await AssertConnected(merryClient, pippinClient);
        await Cleanup(merryClient, pippinClient);
    }

    [Test]
    [Description("Merry: Outgoing, Pippin: Incoming")]
    public async Task CanConnectWhenState_Merry_Outgoing_Pippin_Incoming()
    {
        var merryClient = await LoginAsOwner(Identities.Merry);
        var pippinClient = await LoginAsOwner(Identities.Pippin);

        await SendConnectionRequestTo(merryClient, pippinClient.Identity);

        //
        // Assert state is ready for test
        //

        var pippinInfo = await GetOutgoingSentRequestTo(merryClient, pippinClient.Identity);
        Assert.That(pippinInfo, Is.Not.Null);

        var merryInfo = await GetIncomingRequestFrom(pippinClient, merryClient.Identity);
        Assert.That(merryInfo, Is.Not.Null);

        //
        // They try to reconnect again fully
        //
        await Connect(merryClient, pippinClient);
        await AssertConnected(merryClient, pippinClient);
        await Cleanup(merryClient, pippinClient);
    }

    [Test]
    [Description("Merry: Outgoing, Pippin: None")]
    public async Task CanConnectWhenState_Merry_Outgoing_Pippin_None()
    {
        var merryClient = await LoginAsOwner(Identities.Merry);
        var pippinClient = await LoginAsOwner(Identities.Pippin);

        await SendConnectionRequestTo(merryClient, pippinClient.Identity);

        await DeleteConnectionRequestFrom(pippinClient, merryClient.Identity);

        //
        // Assert state is ready for test
        //

        var pippinInfo = await GetOutgoingSentRequestTo(merryClient, pippinClient.Identity);
        Assert.That(pippinInfo, Is.Not.Null);

        var merryInfo = await GetIncomingRequestFrom(pippinClient, merryClient.Identity);
        Assert.That(merryInfo, Is.Null);

        //
        // They try to reconnect again fully
        //
        await Connect(merryClient, pippinClient);
        await AssertConnected(merryClient, pippinClient);
        await Cleanup(merryClient, pippinClient);
    }

    [Test]
    [Description("Merry: Incoming, Pippin: Connected")]
    public async Task Will_AutoAccept_WhenState_Merry_Incoming_Pippin_Connected()
    {
        var merryClient = await LoginAsOwner(Identities.Merry);
        var pippinClient = await LoginAsOwner(Identities.Pippin);

        await Connect(merryClient, pippinClient);
        await AssertConnected(merryClient, pippinClient);

        await DisconnectFrom(merryClient, pippinClient.Identity, notifyRemote: false);
        await SendConnectionRequestTo(pippinClient, merryClient.Identity);

        //
        // Assert state is ready for test
        //

        var pippinInfo = await GetIncomingRequestFrom(merryClient, pippinClient.Identity);
        Assert.That(pippinInfo, Is.Not.Null);

        var merryInfo = await GetConnectionInfo(pippinClient, merryClient.Identity);
        Assert.That(merryInfo.Status, Is.EqualTo(ConnectionStatus.Connected));

        //
        // Send the request; it should be auto-approved
        //
        await SendConnectionRequestTo(merryClient, pippinClient.Identity, new List<GuidId>());

        await AssertConnected(merryClient, pippinClient);
        await Cleanup(merryClient, pippinClient);
    }

    [Test]
    [Description("Merry: Incoming, Pippin: Connected")]
    public async Task WillFailToAutoAccept_WhenState_Merry_Incoming_Pippin_Connected_and_Pippin_No_outgoing()
    {
        var merryClient = await LoginAsOwner(Identities.Merry);
        var pippinClient = await LoginAsOwner(Identities.Pippin);

        await Connect(merryClient, pippinClient);
        await AssertConnected(merryClient, pippinClient);

        await DisconnectFrom(merryClient, pippinClient.Identity, notifyRemote: false);
        await SendConnectionRequestTo(pippinClient, merryClient.Identity);

        // ensure pippin does not have an outgoing request
        await DeleteSentRequestTo(pippinClient, merryClient.Identity);

        //
        // Assert state is ready for test
        //

        var pippinInfo = await GetIncomingRequestFrom(merryClient, pippinClient.Identity);
        Assert.That(pippinInfo, Is.Not.Null);

        var merryInfo = await GetConnectionInfo(pippinClient, merryClient.Identity);
        Assert.That(merryInfo.Status, Is.EqualTo(ConnectionStatus.Connected));

        //
        // Send the request; it should be auto-approved but will fail because there is no outgoing request from pippin
        //
        var response = await SendConnectionRequestRaw(merryClient, pippinClient.Identity, new List<GuidId>());

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        await Cleanup(merryClient, pippinClient);
    }

    [Test]
    [Description("Merry: Incoming, Pippin: Outgoing")]
    public void CanConnectWhenState_Merry_Incoming_Pippin_Outgoing()
    {
        Assert.Pass("Already tested above");
    }

    [Test]
    [Description("Merry: Incoming, Pippin: Incoming")]
    public async Task WillAutoConnectWhenState_Merry_Incoming_Pippin_Incoming()
    {
        var merryClient = await LoginAsOwner(Identities.Merry);
        var pippinClient = await LoginAsOwner(Identities.Pippin);

        await SendConnectionRequestTo(pippinClient, merryClient.Identity);
        await SendConnectionRequestTo(merryClient, pippinClient.Identity);

        await AssertConnected(merryClient, pippinClient);

        await Cleanup(merryClient, pippinClient);
    }

    [Test]
    [Description("Merry: Incoming, Pippin: None")]
    public async Task FailToConnect_Merry_Incoming_And_Pippin_Has_Deleted_Outgoing_Request()
    {
        var merryClient = await LoginAsOwner(Identities.Merry);
        var pippinClient = await LoginAsOwner(Identities.Pippin);

        await SendConnectionRequestTo(pippinClient, merryClient.Identity);
        await DeleteSentRequestTo(pippinClient, merryClient.Identity);

        //
        // Assert state is ready for test
        //

        Assert.That(await GetIncomingRequestFrom(merryClient, pippinClient.Identity), Is.Not.Null);
        Assert.That(await GetOutgoingSentRequestTo(merryClient, pippinClient.Identity), Is.Null);

        Assert.That(await GetIncomingRequestFrom(pippinClient, merryClient.Identity), Is.Null);
        Assert.That(await GetOutgoingSentRequestTo(pippinClient, merryClient.Identity), Is.Null);


        var response = await SendConnectionRequestRaw(merryClient, pippinClient.Identity, new List<GuidId>());
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        await DeleteConnectionRequestFrom(merryClient, pippinClient.Identity);
    }

    [Test]
    [Description("Merry: None, Pippin: Connected")]
    public async Task CanConnectWhenState_Merry_None_Pippin_Connected()
    {
        var merryClient = await LoginAsOwner(Identities.Merry);
        var pippinClient = await LoginAsOwner(Identities.Pippin);

        await merryClient.Admin.DisableAutoAcceptIntroductions();
        await pippinClient.Admin.DisableAutoAcceptIntroductions();

        await SendConnectionRequestTo(pippinClient, merryClient.Identity);
        await AcceptConnectionRequest(merryClient, pippinClient.Identity);

        await DisconnectFrom(merryClient, pippinClient.Identity);

        //
        // Assert state is ready for test
        //

        Assert.That((await GetConnectionInfo(merryClient, pippinClient.Identity)).Status, Is.EqualTo(ConnectionStatus.None));

        //
        // They try to reconnect again fully
        //
        await Connect(merryClient, pippinClient);
        await AssertConnected(merryClient, pippinClient);
        await Cleanup(merryClient, pippinClient);
    }

    [Test]
    [Description("Merry: None, Pippin: Outgoing")]
    public async Task CanConnectWhenState_Merry_None_Pippin_Outgoing()
    {
        var merryClient = await LoginAsOwner(Identities.Merry);
        var pippinClient = await LoginAsOwner(Identities.Pippin);

        await merryClient.Admin.DisableAutoAcceptIntroductions();
        await pippinClient.Admin.DisableAutoAcceptIntroductions();

        await SendConnectionRequestTo(pippinClient, merryClient.Identity);
        await DeleteConnectionRequestFrom(merryClient, pippinClient.Identity);

        //
        // Assert state is ready for test
        //

        Assert.That(await GetOutgoingSentRequestTo(pippinClient, merryClient.Identity), Is.Not.Null);
        Assert.That(await GetIncomingRequestFrom(pippinClient, merryClient.Identity), Is.Null);

        Assert.That(await GetIncomingRequestFrom(merryClient, pippinClient.Identity), Is.Null);
        Assert.That(await GetOutgoingSentRequestTo(merryClient, pippinClient.Identity), Is.Null);

        //
        // They try to reconnect again fully
        //
        await Connect(merryClient, pippinClient);
        await AssertConnected(merryClient, pippinClient);
        await Cleanup(merryClient, pippinClient);
    }

    [Test]
    [Description("Merry: None, Pippin: Incoming")]
    public async Task CanConnectWhenState_Merry_None_Pippin_Incoming()
    {
        var merryClient = await LoginAsOwner(Identities.Merry);
        var pippinClient = await LoginAsOwner(Identities.Pippin);

        await SendConnectionRequestTo(merryClient, pippinClient.Identity);
        await DeleteSentRequestTo(merryClient, pippinClient.Identity);

        //
        // Assert state is ready for test
        //

        Assert.That(await GetIncomingRequestFrom(pippinClient, merryClient.Identity), Is.Not.Null);
        Assert.That(await GetOutgoingSentRequestTo(pippinClient, merryClient.Identity), Is.Null);

        Assert.That(await GetIncomingRequestFrom(merryClient, pippinClient.Identity), Is.Null);
        Assert.That(await GetOutgoingSentRequestTo(merryClient, pippinClient.Identity), Is.Null);

        //
        // They try to reconnect again fully
        //
        await Connect(merryClient, pippinClient);
        await AssertConnected(merryClient, pippinClient);
        await Cleanup(merryClient, pippinClient);
    }


    [Test]
    public async Task CanAutoAcceptIncomingConnectionRequestsWhenOutgoingRequestExists()
    {
        var merryClient = await LoginAsOwner(Identities.Merry);
        var pippinClient = await LoginAsOwner(Identities.Pippin);

        await SendConnectionRequestTo(merryClient, pippinClient.Identity);
        await SendConnectionRequestTo(pippinClient, merryClient.Identity);

        //
        await AssertConnected(merryClient, pippinClient);

        //
        Assert.That(await GetOutgoingSentRequestTo(merryClient, pippinClient.Identity), Is.Null);
        Assert.That(await GetIncomingRequestFrom(merryClient, pippinClient.Identity), Is.Null);

        await Cleanup(merryClient, pippinClient);
    }


    [Test]
    [Ignore("TODO - never written; carried over from the original, which left it commented out")]
    public void Reject_ConnectionRequest_when_SenderIsBlocked()
    {
        Assert.Inconclusive("TODO");
    }

    [Test]
    [Description("If the outgoing connection request is deleted before attempting establish a connection; the accept connection request will fail")]
    [Ignore("TODO - never written; carried over from the original, which left it commented out")]
    public void FailToAcceptConnectionRequest_when_SendersOutgoingRequestWasDeleted()
    {
        /*
         * 1. merry sends connection request to frodo
         * 2. merry deletes outgoing request to frodo
         * 3. Frodo accepts merry's connection request
         * 4. Frodo receives error, system deletes merry's connection request, frodo is told to resend it
         */
        Assert.Inconclusive("TODO");
    }

    [Test]
    [Ignore("TODO - never written; carried over from the original, which left it commented out")]
    public void WhenConnectionIsSevered_BothPartiesHaveICRDeleted()
    {
        Assert.Inconclusive("TODO - should we support this?");
    }

    [Test]
    public async Task CanReceiveMultipleConnectionRequestsFromSameSender()
    {
        var merryClient = await LoginAsOwner(Identities.Merry);
        var pippinClient = await LoginAsOwner(Identities.Pippin);

        await SendConnectionRequestTo(merryClient, pippinClient.Identity);
        Assert.That(await GetIncomingRequestFrom(pippinClient, merryClient.Identity), Is.Not.Null);

        await SendConnectionRequestTo(merryClient, pippinClient.Identity);
        Assert.That(await GetIncomingRequestFrom(pippinClient, merryClient.Identity), Is.Not.Null);
    }

    // -------------------------------------------------------------------------------------------
    // Local stand-ins for the V1 OwnerApiClient.Network helpers.
    // -------------------------------------------------------------------------------------------

    private static async Task Connect(OwnerSession senderOwnerClient, OwnerSession recipientOwnerClient)
    {
        //Note
        await senderOwnerClient.Admin.DisableAutoAcceptIntroductions();
        await recipientOwnerClient.Admin.DisableAutoAcceptIntroductions();

        await SendConnectionRequestTo(senderOwnerClient, recipientOwnerClient.Identity, new List<GuidId>());
        await AcceptConnectionRequest(recipientOwnerClient, senderOwnerClient.Identity, new List<GuidId>());
    }

    private static async Task AssertConnected(OwnerSession senderOwnerClient, OwnerSession recipientOwnerClient)
    {
        //
        // Test Sender's record on recipient server
        //

        var senderConnectionInfoOnRecipientIdentity = await GetConnectionInfo(recipientOwnerClient, senderOwnerClient.Identity);
        Assert.That(senderConnectionInfoOnRecipientIdentity.Status, Is.EqualTo(ConnectionStatus.Connected));
        Assert.That(await GetIncomingRequestFrom(recipientOwnerClient, senderOwnerClient.Identity), Is.Null);
        Assert.That(await GetOutgoingSentRequestTo(recipientOwnerClient, senderOwnerClient.Identity), Is.Null);

        //
        // Test recipient's record on sender server
        //
        var recipientConnectionInfo = await GetConnectionInfo(senderOwnerClient, recipientOwnerClient.Identity);
        Assert.That(recipientConnectionInfo.Status, Is.EqualTo(ConnectionStatus.Connected));

        Assert.That(await GetIncomingRequestFrom(senderOwnerClient, recipientOwnerClient.Identity), Is.Null);
        Assert.That(await GetOutgoingSentRequestTo(senderOwnerClient, recipientOwnerClient.Identity), Is.Null);
    }

    private static async Task Cleanup(OwnerSession merryClient, OwnerSession pippinClient)
    {
        await DeleteConnectionRequestFrom(pippinClient, merryClient.Identity);
        await DeleteSentRequestTo(pippinClient, merryClient.Identity);
        await DisconnectFrom(pippinClient, merryClient.Identity);

        await DeleteConnectionRequestFrom(merryClient, pippinClient.Identity);
        await DeleteSentRequestTo(merryClient, pippinClient.Identity);
        await DisconnectFrom(merryClient, pippinClient.Identity);
    }

    /// <summary>
    /// The <see cref="TestIdentity.ContactData"/> for a domain. The V1 originals reached this
    /// through <c>TestIdentities.InitializedIdentities</c>, which only <c>WebScaffold</c> populates;
    /// the fast host never calls <c>SetCurrent</c>, so the lookup goes to the static defaults.
    /// </summary>
    private static ContactRequestData ContactDataFor(OdinId identity) =>
        TestIdentities.Defaults.Single(i => i.OdinId == identity).ContactData;

    private static async Task SendConnectionRequestTo(OwnerSession owner, OdinId recipient,
        IEnumerable<GuidId> circlesGrantedToRecipient = null)
    {
        var response = await SendConnectionRequestRaw(owner, recipient, circlesGrantedToRecipient);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content, Is.True, "Failed sending the request");
    }

    private static async Task<ApiResponse<bool>> SendConnectionRequestRaw(OwnerSession owner, OdinId recipient,
        IEnumerable<GuidId> circlesGrantedToRecipient = null)
    {
        var svc = owner.RefitFor<IRefitOwnerCircleNetworkRequests>();

        var id = Guid.NewGuid();
        var requestHeader = new ConnectionRequestHeader()
        {
            Id = id,
            Recipient = recipient,
            Message = "Please add me",

            // Carried defect: the original helper put the *recipient's* contact data on the sender's
            // outgoing request. Nothing in this fixture reads contact data back, so it is inert;
            // reproduced rather than corrected so the port stays a move.
            ContactData = ContactDataFor(recipient),
            CircleIds = circlesGrantedToRecipient?.ToList()
        };

        return await svc.SendConnectionRequest(requestHeader);
    }

    private static async Task AcceptConnectionRequest(OwnerSession owner, OdinId sender,
        IEnumerable<GuidId> circleIdsGrantedToSender = null)
    {
        var svc = owner.RefitFor<IRefitOwnerCircleNetworkRequests>();

        var header = new AcceptRequestHeader()
        {
            Sender = sender,
            CircleIds = circleIdsGrantedToSender,
            ContactData = ContactDataFor(owner.Identity)
        };

        var acceptResponse = await svc.AcceptConnectionRequest(header);
        Assert.That(acceptResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private static async Task<ConnectionRequestResponse> GetIncomingRequestFrom(OwnerSession owner, OdinId sender) =>
        (await owner.Connections.GetIncomingRequestFrom(sender)).Content;

    private static async Task<ConnectionRequestResponse> GetOutgoingSentRequestTo(OwnerSession owner, OdinId recipient) =>
        (await owner.Connections.GetOutgoingSentRequestTo(recipient)).Content;

    private static async Task DeleteConnectionRequestFrom(OwnerSession owner, OdinId sender)
    {
        var svc = owner.RefitFor<IRefitOwnerCircleNetworkRequests>();

        var deleteResponse = await svc.DeletePendingRequest(new OdinIdRequest() { OdinId = sender });
        Assert.That(deleteResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var getResponse = await svc.GetPendingRequest(new OdinIdRequest() { OdinId = sender });
        Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    private static async Task DeleteSentRequestTo(OwnerSession owner, OdinId recipient)
    {
        var svc = owner.RefitFor<IRefitOwnerCircleNetworkRequests>();

        var deleteResponse = await svc.DeleteSentRequest(new OdinIdRequest() { OdinId = recipient });
        Assert.That(deleteResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var getResponse = await svc.GetSentRequest(new OdinIdRequest() { OdinId = recipient });
        Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    private static async Task DisconnectFrom(OwnerSession owner, OdinId recipient, bool notifyRemote = true)
    {
        var svc = owner.RefitFor<IRefitOwnerCircleNetworkConnections>();
        var disconnectResponse = await svc.Disconnect(new OdinIdRequest() { OdinId = recipient }, notifyRemote);
        // Content is false when there was nothing connected to disconnect (e.g. the peer already
        // severed the connection via a prior reciprocal-disconnect notification) -- that's not a failure.
        Assert.That(disconnectResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await AssertConnectionStatus(owner, recipient, ConnectionStatus.None);
    }
}
