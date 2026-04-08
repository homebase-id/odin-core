using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Odin.Services.Drives.Reactions;
using Odin.Services.Peer.Incoming.Reactions;
using Refit;

namespace Odin.Services.Peer.Outgoing.Drive.Reactions
{
    public interface IPeerReactionHttpClient
    {
        private const string RootPath = PeerApiPathConstants.ReactionsV1;

        [Post(RootPath + "/add")]
        Task<ApiResponse<HttpContent>> AddReaction([Body] SharedSecretEncryptedTransitPayload payload, CancellationToken cancellationToken = default);

        [Post(RootPath + "/list")]
        Task<ApiResponse<GetReactionsPerimeterResponse>> GetReactions([Body] SharedSecretEncryptedTransitPayload payload, CancellationToken cancellationToken = default);

        [Post(RootPath + "/delete")]
        Task<ApiResponse<HttpContent>> DeleteReactionContent([Body] SharedSecretEncryptedTransitPayload file, CancellationToken cancellationToken = default);

        [Post(RootPath + "/summary")]
        Task<ApiResponse<GetReactionCountsResponse>> GetReactionCountsByFile([Body] SharedSecretEncryptedTransitPayload file, CancellationToken cancellationToken = default);

        [Post(RootPath + "/listbyidentity")]
        Task<ApiResponse<List<string>>> GetReactionsByIdentity([Body] SharedSecretEncryptedTransitPayload file, CancellationToken cancellationToken = default);

    }
}