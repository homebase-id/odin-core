using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Drives.Reactions;
using Odin.Core.Services.Peer.ReceivingHost.Reactions;
using Odin.Core.Services.Peer.SendingHost;
using Odin.Hosting.Controllers.ClientToken.App;
using Refit;

namespace Odin.Hosting.Tests.AppAPI.ApiClient.TransitQuery
{
    public interface IRefitAppTransitReactionSender
    {
        private const string RootEndpoint = AppApiPathConstants.TransitReactionContentV1;

        [Post(RootEndpoint + "/add")]
        Task<ApiResponse<HttpContent>> AddReaction([Body] TransitAddReactionRequest request);

        [Post(RootEndpoint + "/list")]
        Task<ApiResponse<GetReactionsPerimeterResponse>> GetAllReactions([Body] TransitGetReactionsRequest file);

        [Post(RootEndpoint + "/delete")]
        Task<ApiResponse<HttpContent>> DeleteReactionContent([Body] TransitDeleteReactionRequest file);

        [Post(RootEndpoint + "/deleteall")]
        Task<ApiResponse<HttpContent>> DeleteAllReactionsOnFile([Body] TransitDeleteReactionRequest file);

        [Post(RootEndpoint + "/summary")]
        Task<ApiResponse<GetReactionCountsResponse>> GetReactionCountsByFile([Body] TransitGetReactionsRequest file);

        [Post(RootEndpoint + "/listbyidentity")]
        Task<ApiResponse<List<string>>> GetReactionsByIdentity([Body] TransitGetReactionsByIdentityRequest file);
    }
}