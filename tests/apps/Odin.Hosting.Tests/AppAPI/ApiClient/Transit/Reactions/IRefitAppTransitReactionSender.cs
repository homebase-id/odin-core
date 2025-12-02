using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Services.Drives.Reactions;
using Odin.Services.Peer.Incoming.Reactions;
using Odin.Services.Peer.Outgoing;
using Odin.Services.Peer.Outgoing.Drive.Reactions;
using Odin.Hosting.Controllers.ClientToken.App;
using Refit;

namespace Odin.Hosting.Tests.AppAPI.ApiClient.Transit.Reactions
{
    public interface IRefitAppTransitReactionSender
    {
        private const string RootEndpoint = AppApiPathConstantsV1.PeerReactionContentV1;

        [Post(RootEndpoint + "/add")]
        Task<ApiResponse<HttpContent>> AddReaction([Body] PeerAddReactionRequest request);

        [Post(RootEndpoint + "/list")]
        Task<ApiResponse<GetReactionsPerimeterResponse>> GetAllReactions([Body] PeerGetReactionsRequest file);

        [Post(RootEndpoint + "/delete")]
        Task<ApiResponse<HttpContent>> DeleteReactionContent([Body] PeerDeleteReactionRequest file);

        [Post(RootEndpoint + "/deleteall")]
        Task<ApiResponse<HttpContent>> DeleteAllReactionsOnFile([Body] PeerDeleteReactionRequest file);

        [Post(RootEndpoint + "/summary")]
        Task<ApiResponse<GetReactionCountsResponse>> GetReactionCountsByFile([Body] PeerGetReactionsRequest file);

        [Post(RootEndpoint + "/listbyidentity")]
        Task<ApiResponse<List<string>>> GetReactionsByIdentity([Body] PeerGetReactionsByIdentityRequest file);
    }
}