using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Hosting.Controllers.ClientToken.Shared.Drive;
using Odin.Services.Drives.Reactions;
using Odin.Services.Peer.Incoming.Reactions;
using Odin.Services.Peer.Outgoing.Drive.Reactions;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Transit
{
    public interface IUniversalRefitPeerReaction
    {
        private const string RootEndpoint = "/transit/reactions";
        
        [Post(RootEndpoint + "/add")]
        Task<ApiResponse<HttpContent>> AddReaction([Body] PeerAddReactionRequest request);

        [Post(RootEndpoint + "/list")]
        Task<ApiResponse<GetReactionsPerimeterResponse>> GetAllReactions([Body] PeerGetReactionsRequest request);

        [Post(RootEndpoint + "/delete")]
        Task<ApiResponse<HttpContent>> DeleteReaction([Body] PeerDeleteReactionRequest request);

        [Post(RootEndpoint + "/deleteall")]
        Task<ApiResponse<HttpContent>> DeleteAllReactionsOnFile([Body] PeerDeleteReactionRequest request);

        [Post(RootEndpoint + "/summary")]
        Task<ApiResponse<GetReactionCountsResponse>> GetReactionCountsByFile([Body] PeerGetReactionsRequest request);

        [Post(RootEndpoint + "/listbyidentity")]
        Task<ApiResponse<List<string>>> GetReactionsByIdentity([Body] PeerGetReactionsByIdentityRequest request);
    }
}