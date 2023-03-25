using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Refit;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.Reactions;
using Youverse.Core.Services.Transit.ReceivingHost;
using Youverse.Core.Services.Transit.ReceivingHost.Quarantine;

namespace Youverse.Core.Services.Transit.SendingHost
{
    /// <summary>
    /// The interface for querying from a host to another host
    /// </summary>
    public interface ITransitHostEmojiHttpClient
    {
        private const string RootPath = "/api/perimeter/transit/host/reactions";

        [Post(RootPath + "/add")]
        Task<ApiResponse<HttpContent>> AddReaction([Body]SharedSecretEncryptedTransitPayload payload);

        [Post(RootPath + "/list")]
        Task<ApiResponse<GetReactionsResponse>> GetReactions([Body]SharedSecretEncryptedTransitPayload payload);
        
        [Post(RootPath + "/delete")]
        Task<ApiResponse<HttpContent>> DeleteEmojiReaction([Body] SharedSecretEncryptedTransitPayload file);

        [Post(RootPath + "/deleteall")]
        Task<ApiResponse<HttpContent>> DeleteAllReactionsOnFile([Body] SharedSecretEncryptedTransitPayload file);

        [Post(RootPath + "/summary")]
        Task<ApiResponse<GetReactionCountsResponse>> GetReactionCountsByFile([Body] SharedSecretEncryptedTransitPayload file);

        [Post(RootPath + "/listbyidentity")]
        Task<ApiResponse<List<string>>> GetReactionsByIdentity([Body] SharedSecretEncryptedTransitPayload file);
        
    }
}