using System.Collections;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;

namespace Odin.Hosting.Tests._Universal.Owner.Connections.Introductions.AutoAcceptVariations;

internal static class IntroductionTestUtils
{
    public static async Task<bool> HasReceivedIntroducedConnectionRequestFromIntroducee(OwnerApiClientRedux owner, OdinId introducee)
    {
        var response = await owner.Connections.GetIncomingRequestFrom(introducee);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        ClassicAssert.IsTrue(response.IsSuccessStatusCode);
        return response.Content != null && response.Content.ConnectionRequestOrigin == ConnectionRequestOrigin.Introduction;
    }


    public static async Task<bool> HasSentIntroducedConnectionRequestToIntroducee(OwnerApiClientRedux owner, OdinId introducee)
    {
        var response = await owner.Connections.GetOutgoingSentRequestTo(introducee);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        ClassicAssert.IsTrue(response.IsSuccessStatusCode);
        return response.Content != null && response.Content.ConnectionRequestOrigin == ConnectionRequestOrigin.Introduction;
    }

    public static async Task<bool> HasIntroductionFromIdentity(OwnerApiClientRedux owner, OdinId introducee)
    {
        var response = await owner.Connections.GetReceivedIntroductions();
        ClassicAssert.IsTrue(response.IsSuccessStatusCode);
        return response.Content.Any(intro => intro.Identity == introducee);
    }

    public static async Task<bool> IsConnectedWithExpectedOrigin(OwnerApiClientRedux owner, OdinId introducee,
        ConnectionRequestOrigin expectedOrigin)
    {
        var getConnectionInfoResponse = await owner.Network.GetConnectionInfo(introducee);
        ClassicAssert.IsTrue(getConnectionInfoResponse.IsSuccessStatusCode);

        bool isIntroduction = getConnectionInfoResponse.Content!.ConnectionRequestOrigin == expectedOrigin &&
                              getConnectionInfoResponse.Content.Status == ConnectionStatus.Connected;

        return isIntroduction;
    }

    public static async Task<bool> IsConnected(OwnerApiClientRedux owner, OdinId introducee)
    {
        var getConnectionInfoResponse = await owner.Network.GetConnectionInfo(introducee);
        ClassicAssert.IsTrue(getConnectionInfoResponse.IsSuccessStatusCode);

        bool isIntroduction = getConnectionInfoResponse.Content!.Status == ConnectionStatus.Connected;

        return isIntroduction;
    }

    public static async Task<OwnerApiClientRedux> PrepareIntroducer(WebScaffold scaffold)
    {
        //you have 3 hobbits

        // Frodo is connected to Sam and Merry
        // Sam and Merry are not connected

        var frodo = scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var sam = scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var merry = scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);

        await frodo.Connections.SendConnectionRequest(sam.OdinId, []);
        await frodo.Connections.SendConnectionRequest(merry.OdinId, []);

        await merry.Connections.AcceptConnectionRequest(frodo.OdinId);
        await sam.Connections.AcceptConnectionRequest(frodo.OdinId);

        return frodo;
    }

    public static async Task Cleanup(WebScaffold scaffold)
    {
        var frodo = scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var sam = scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var merry = scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);

        //Note disconnecting does not unblock
        await merry.Network.UnblockConnection(sam.OdinId);
        await sam.Network.UnblockConnection(merry.OdinId);

        await frodo.Connections.DisconnectFrom(sam.OdinId);
        await frodo.Connections.DisconnectFrom(merry.OdinId);

        await merry.Connections.DisconnectFrom(frodo.OdinId);
        await sam.Connections.DisconnectFrom(frodo.OdinId);

        await merry.Connections.DisconnectFrom(sam.OdinId);
        await sam.Connections.DisconnectFrom(merry.OdinId);

        await merry.Connections.DeleteAllIntroductions();
        await sam.Connections.DeleteAllIntroductions();
    }
}