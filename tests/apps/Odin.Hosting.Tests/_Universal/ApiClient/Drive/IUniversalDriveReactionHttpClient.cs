using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Services.Authentication.Owner;
using Odin.Services.Drives;
using Odin.Services.Drives.Reactions;
using Odin.Hosting.Controllers.Base.Drive;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Drive
{
    // SEB:TODO delete this
    public interface IUniversalDriveReactionHttpClient
    {
        private const string ReactionRootEndpoint = "/drive/files/reactions";

        [Post(ReactionRootEndpoint + "/add")]
        Task<ApiResponse<HttpContent>> AddReaction([Body] AddReactionRequest request);

        [Post(ReactionRootEndpoint + "/delete")]
        Task<ApiResponse<HttpContent>> DeleteReaction([Body] DeleteReactionRequest request);

        [Post(ReactionRootEndpoint + "/deleteall")]
        Task<ApiResponse<HttpContent>> DeleteReactions([Body] DeleteReactionRequest request);

        [Post(ReactionRootEndpoint + "/list")]
        Task<ApiResponse<GetReactionsResponse>> GetReactions([Body] ExternalFileIdentifier file);
        
        [Post(ReactionRootEndpoint + "/summary")]
        Task<ApiResponse<GetReactionCountsResponse>> GetReactionCountsByFile([Body] GetReactionsRequest file);

        [Post(ReactionRootEndpoint + "/listbyidentity")]
        Task<ApiResponse<List<string>>> GetReactionsByIdentity([Body] GetReactionsByIdentityRequest file);
    }
}