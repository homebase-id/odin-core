using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Services.Drives.Reactions;
using Odin.Core.Services.Peer.Incoming.Reactions;
using Odin.Core.Services.Peer.Outgoing;
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

    public async Task<ApiResponse<HttpContent>> AddReaction(TransitAddReactionRequest request, FileSystemType fst = FileSystemType.Standard)
    {
        var client = CreateAppApiHttpClient(_token, fst);
        {
            var svc = RefitCreator.RestServiceFor<IRefitAppTransitReactionSender>(client, _token.SharedSecret);
            var apiResponse = await svc.AddReaction(request);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<GetReactionsPerimeterResponse>> GetAllReactions([Body] TransitGetReactionsRequest request, FileSystemType fst = FileSystemType.Standard)
    {
        var client = CreateAppApiHttpClient(_token, fst);
        {
            var svc = RefitCreator.RestServiceFor<IRefitAppTransitReactionSender>(client, _token.SharedSecret);
            var apiResponse = await svc.GetAllReactions(request);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<HttpContent>> DeleteReactionContent([Body] TransitDeleteReactionRequest request, FileSystemType fst = FileSystemType.Standard)
    {
        var client = CreateAppApiHttpClient(_token, fst);
        {
            var svc = RefitCreator.RestServiceFor<IRefitAppTransitReactionSender>(client, _token.SharedSecret);
            var apiResponse = await svc.DeleteReactionContent(request);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<HttpContent>> DeleteAllReactionsOnFile([Body] TransitDeleteReactionRequest request, FileSystemType fst = FileSystemType.Standard)
    {
        var client = CreateAppApiHttpClient(_token, fst);
        {
            var svc = RefitCreator.RestServiceFor<IRefitAppTransitReactionSender>(client, _token.SharedSecret);
            var apiResponse = await svc.DeleteAllReactionsOnFile(request);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<GetReactionCountsResponse>> GetReactionCountsByFile([Body] TransitGetReactionsRequest request, FileSystemType fst = FileSystemType.Standard)
    {
        var client = CreateAppApiHttpClient(_token, fst);
        {
            var svc = RefitCreator.RestServiceFor<IRefitAppTransitReactionSender>(client, _token.SharedSecret);
            var apiResponse = await svc.GetReactionCountsByFile(request);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<List<string>>> GetReactionsByIdentity([Body] TransitGetReactionsByIdentityRequest request, FileSystemType fst = FileSystemType.Standard)
    {
        var client = CreateAppApiHttpClient(_token, fst);
        {
            var svc = RefitCreator.RestServiceFor<IRefitAppTransitReactionSender>(client, _token.SharedSecret);
            var apiResponse = await svc.GetReactionsByIdentity(request);
            return apiResponse;
        }
    }
}