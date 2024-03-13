using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Services.Drives.Reactions;
using Odin.Services.Peer.Incoming.Reactions;
using Odin.Services.Peer.Outgoing;
using Odin.Services.Peer.Outgoing.Drive.Reactions;
using Odin.Core.Storage;
using Odin.Hosting.Tests.AppAPI.ApiClient.Base;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Refit;

namespace Odin.Hosting.Tests.AppAPI.ApiClient.Transit.Reactions;

public class AppTransitReactionSenderApiClient : AppApiClientBase
{
    private readonly AppClientToken _token;

    public AppTransitReactionSenderApiClient(OwnerApiTestUtils ownerApiTestUtils, AppClientToken token) : base(ownerApiTestUtils)
    {
        _token = token;
    }

    public async Task<ApiResponse<HttpContent>> AddReaction(PeerAddReactionRequest request, FileSystemType fst = FileSystemType.Standard)
    {
        var client = CreateAppApiHttpClient(_token, fst);
        {
            var svc = RefitCreator.RestServiceFor<IRefitAppTransitReactionSender>(client, _token.SharedSecret);
            var apiResponse = await svc.AddReaction(request);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<GetReactionsPerimeterResponse>> GetAllReactions([Body] PeerGetReactionsRequest request, FileSystemType fst = FileSystemType.Standard)
    {
        var client = CreateAppApiHttpClient(_token, fst);
        {
            var svc = RefitCreator.RestServiceFor<IRefitAppTransitReactionSender>(client, _token.SharedSecret);
            var apiResponse = await svc.GetAllReactions(request);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<HttpContent>> DeleteReactionContent([Body] PeerDeleteReactionRequest request, FileSystemType fst = FileSystemType.Standard)
    {
        var client = CreateAppApiHttpClient(_token, fst);
        {
            var svc = RefitCreator.RestServiceFor<IRefitAppTransitReactionSender>(client, _token.SharedSecret);
            var apiResponse = await svc.DeleteReactionContent(request);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<HttpContent>> DeleteAllReactionsOnFile([Body] PeerDeleteReactionRequest request, FileSystemType fst = FileSystemType.Standard)
    {
        var client = CreateAppApiHttpClient(_token, fst);
        {
            var svc = RefitCreator.RestServiceFor<IRefitAppTransitReactionSender>(client, _token.SharedSecret);
            var apiResponse = await svc.DeleteAllReactionsOnFile(request);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<GetReactionCountsResponse>> GetReactionCountsByFile([Body] PeerGetReactionsRequest request, FileSystemType fst = FileSystemType.Standard)
    {
        var client = CreateAppApiHttpClient(_token, fst);
        {
            var svc = RefitCreator.RestServiceFor<IRefitAppTransitReactionSender>(client, _token.SharedSecret);
            var apiResponse = await svc.GetReactionCountsByFile(request);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<List<string>>> GetReactionsByIdentity([Body] PeerGetReactionsByIdentityRequest request, FileSystemType fst = FileSystemType.Standard)
    {
        var client = CreateAppApiHttpClient(_token, fst);
        {
            var svc = RefitCreator.RestServiceFor<IRefitAppTransitReactionSender>(client, _token.SharedSecret);
            var apiResponse = await svc.GetReactionsByIdentity(request);
            return apiResponse;
        }
    }
}