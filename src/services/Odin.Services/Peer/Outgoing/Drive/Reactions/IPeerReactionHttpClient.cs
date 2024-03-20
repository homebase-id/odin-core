using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Services.Drives.Reactions;
using Odin.Services.Peer.Incoming.Reactions;
using Refit;

namespace Odin.Services.Peer.Outgoing.Drive.Reactions
{
    /// <summary>
    /// The interface for querying from a host to another host
    /// </summary>
    public interface IPeerReactionHttpClient
    {
        private const string RootPath = PeerApiPathConstants.ReactionsV1;

        [Post(RootPath + "/add")]
        Task<ApiResponse<bool>> AddReaction([Body]SharedSecretEncryptedTransitPayload payload);

        [Post(RootPath + "/list")]
        Task<ApiResponse<GetReactionsPerimeterResponse>> GetReactions([Body]SharedSecretEncryptedTransitPayload payload);
        
        [Post(RootPath + "/delete")]
        Task<ApiResponse<HttpContent>> DeleteReactionContent([Body] SharedSecretEncryptedTransitPayload file);

        [Post(RootPath + "/deleteall")]
        Task<ApiResponse<HttpContent>> DeleteAllReactionsOnFile([Body] SharedSecretEncryptedTransitPayload file);

        [Post(RootPath + "/summary")]
        Task<ApiResponse<GetReactionCountsResponse>> GetReactionCountsByFile([Body] SharedSecretEncryptedTransitPayload file);

        [Post(RootPath + "/listbyidentity")]
        Task<ApiResponse<List<string>>> GetReactionsByIdentity([Body] SharedSecretEncryptedTransitPayload file);
        
    }
}