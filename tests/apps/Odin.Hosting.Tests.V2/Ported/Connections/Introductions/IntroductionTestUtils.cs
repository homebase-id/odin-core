#nullable enable
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.ApiClient.Connections;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;

namespace Odin.Hosting.Tests.V2.Ported.Connections.Introductions;

/// <summary>
/// Moved from <c>_Universal/Owner/Connections/Introductions/AutoAcceptVariations/IntroductionTestUtils</c>.
/// The "what does this identity believe about that one" readers the introduction fixtures share,
/// rebound from <c>OwnerApiClientRedux</c> to <see cref="OwnerSession"/>.
/// </summary>
/// <remarks>
/// <para>
/// The original's <c>Cleanup(scaffold)</c> — unblock every pairing, disconnect every pairing, delete
/// every stray request and introduction — is gone. It was pure lifecycle for a <c>WebScaffold</c>
/// that shared identities process-wide, and <c>V2Fixture</c> restores the identity DB before every
/// test. Nothing in it asserted.
/// </para>
/// <para>
/// Carried as found: <see cref="HasSentIntroducedConnectionRequestToIntroducee"/> has no callers and
/// never had any — it is dead in the original too.
/// </para>
/// </remarks>
internal static class IntroductionTestUtils
{
    public static async Task<bool> HasReceivedIntroducedConnectionRequestFromIntroducee(OwnerSession owner, OdinId introducee)
    {
        var response = await owner.Connections.GetIncomingRequestFrom(introducee);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        Assert.That(response.IsSuccessStatusCode, Is.True);
        return response.Content != null && response.Content.ConnectionRequestOrigin == ConnectionRequestOrigin.Introduction;
    }

    public static async Task<bool> HasSentIntroducedConnectionRequestToIntroducee(OwnerSession owner, OdinId introducee)
    {
        var response = await owner.Connections.GetOutgoingSentRequestTo(introducee);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        Assert.That(response.IsSuccessStatusCode, Is.True);
        return response.Content != null && response.Content.ConnectionRequestOrigin == ConnectionRequestOrigin.Introduction;
    }

    public static async Task<bool> HasIntroductionFromIdentity(OwnerSession owner, OdinId introducee)
    {
        var response = await Requests(owner).GetReceivedIntroductions();
        Assert.That(response.IsSuccessStatusCode, Is.True);
        return response.Content!.Any(intro => intro.Identity == introducee);
    }

    public static async Task<bool> IsConnectedWithExpectedOrigin(OwnerSession owner, OdinId introducee,
        ConnectionRequestOrigin expectedOrigin)
    {
        var getConnectionInfoResponse = await owner.Connections.GetConnectionInfo(introducee);
        Assert.That(getConnectionInfoResponse.IsSuccessStatusCode, Is.True);

        bool isIntroduction = getConnectionInfoResponse.Content!.ConnectionRequestOrigin == expectedOrigin &&
                              getConnectionInfoResponse.Content.Status == ConnectionStatus.Connected;

        return isIntroduction;
    }

    public static async Task<bool> IsConnected(OwnerSession owner, OdinId introducee)
    {
        var getConnectionInfoResponse = await owner.Connections.GetConnectionInfo(introducee);
        Assert.That(getConnectionInfoResponse.IsSuccessStatusCode, Is.True);

        bool isIntroduction = getConnectionInfoResponse.Content!.Status == ConnectionStatus.Connected;

        return isIntroduction;
    }

    /// <summary>
    /// You have 3 hobbits. Frodo is connected to Sam and Merry; Sam and Merry are not connected.
    /// Returns the introducer.
    /// </summary>
    public static async Task<OwnerSession> PrepareIntroducer(OwnerSession frodo, OwnerSession sam, OwnerSession merry)
    {
        await frodo.Connections.SendConnectionRequest(sam.Identity, []);
        await frodo.Connections.SendConnectionRequest(merry.Identity, []);

        await merry.Connections.AcceptConnectionRequest(frodo.Identity);
        await sam.Connections.AcceptConnectionRequest(frodo.Identity);

        return frodo;
    }

    /// <summary>
    /// The V1 connection-requests surface as this owner. The introductions endpoints are the system
    /// under test in these fixtures, so they go through Refit rather than <c>owner.Admin</c>.
    /// </summary>
    public static IRefitUniversalCircleNetworkRequests Requests(OwnerSession owner) =>
        owner.RefitFor<IRefitUniversalCircleNetworkRequests>();
}
