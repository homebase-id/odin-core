using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Drives.Reactions;
using Odin.Core.Services.Peer.ReceivingHost.Reactions;
using Odin.Core.Services.Peer.SendingHost;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Transit
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface IUniversalRefitOwnerTransitReaction
    {
        private const string RootEndpoint = OwnerApiPathConstants.TransitReactionContentV1;

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