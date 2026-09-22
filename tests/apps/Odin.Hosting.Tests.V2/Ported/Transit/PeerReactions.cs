using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Transit;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Drives;
using Odin.Services.Peer.Incoming.Reactions;
using Odin.Services.Peer.Outgoing.Drive.Reactions;
using Refit;

namespace Odin.Hosting.Tests.V2.Ported.Transit;

/// <summary>
/// Add / list / delete a reaction on another identity's file, over peer transit. Stands in for the V1
/// <c>TransitApiClient</c>'s reaction methods, which have no <c>_Universal</c> twin —
/// <c>IUniversalRefitOwnerTransitReaction</c> exists but declares no members.
/// </summary>
/// <remarks>
/// Reached through <see cref="OwnerSession.RefitFor{T}"/> on the V1
/// <see cref="IRefitOwnerTransitReaction"/>, whose routes are already absolute
/// (<c>/api/owner/v1/transit/reactions/...</c>) and so pass through the factory's path normalizer
/// untouched. <see cref="AddReactionAsync"/> and <see cref="GetAllReactionsAsync"/> assert success the
/// way the V1 client did; <see cref="DeleteReactionAsync"/> hands the response back, because one of
/// its callers asserts on it.
/// </remarks>
internal static class PeerReactions
{
    public static async Task AddReactionAsync(
        OwnerSession caller, OdinId remoteIdentity, GlobalTransitIdFileIdentifier file, string reactionContent)
    {
        var svc = caller.RefitFor<IRefitOwnerTransitReaction>();
        var response = await svc.AddReaction(new PeerAddReactionRequest
        {
            OdinId = remoteIdentity,
            Request = new AddRemoteReactionRequest
            {
                File = file,
                Reaction = reactionContent
            }
        });

        // 204, not 200: the endpoint answers with no body. The V1 client asserted IsSuccessStatusCode,
        // which covered both; the exact code is asserted here so a failure prints what came back.
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    /// <summary>
    /// Every reaction on <paramref name="file"/>, from the start of the list. The fixtures that call
    /// this all want the same whole-list request, so it is built here rather than at each call site.
    /// </summary>
    public static Task<GetReactionsPerimeterResponse> GetAllReactionsAsync(
        OwnerSession caller, OdinId remoteIdentity, GlobalTransitIdFileIdentifier file, int maxRecords = 100)
        => GetAllReactionsAsync(caller, remoteIdentity, new GetRemoteReactionsRequest
        {
            File = file,
            Cursor = "",
            MaxRecords = maxRecords
        });

    public static async Task<GetReactionsPerimeterResponse> GetAllReactionsAsync(
        OwnerSession caller, OdinId remoteIdentity, GetRemoteReactionsRequest request)
    {
        var svc = caller.RefitFor<IRefitOwnerTransitReaction>();
        var response = await svc.GetAllReactions(new PeerGetReactionsRequest
        {
            OdinId = remoteIdentity,
            Request = request
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return response.Content;
    }

    public static async Task<ApiResponse<System.Net.Http.HttpContent>> DeleteReactionAsync(
        OwnerSession caller, OdinId remoteIdentity, string reaction, GlobalTransitIdFileIdentifier file)
    {
        var svc = caller.RefitFor<IRefitOwnerTransitReaction>();
        return await svc.DeleteReactionContent(new PeerDeleteReactionRequest
        {
            OdinId = remoteIdentity,
            Request = new DeleteReactionRequestByGlobalTransitId
            {
                Reaction = reaction,
                File = file
            }
        });
    }
}
