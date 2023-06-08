using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Services.Drives.Reactions;
using Odin.Core.Services.Transit.ReceivingHost.Reactions;
using Refit;

namespace Odin.Core.Services.Transit.SendingHost
{
    /// <summary>
    /// The interface for querying from a host to another host
    /// </summary>
    public interface ITransitHostReactionHttpClient
    {
        private const string RootPath = "/api/perimeter/transit/host/reactions";

        [Post(RootPath + "/add")]
        Task<ApiResponse<HttpContent>> AddReaction([Body]SharedSecretEncryptedTransitPayload payload);

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