using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Services.Authentication.Owner;
using Odin.Services.Drives;
using Odin.Services.Drives.Reactions;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Controllers.Reactions.DTOs;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Drive
{
    public interface IUniversalDriveReactionHttpClient2
    {
        private const string ReactionRootEndpoint = "/unified-reactions"; // SEB:TODO put this somewhere else

        [Post(ReactionRootEndpoint + "/add")]
        Task<ApiResponse<HttpContent>> AddReaction([Body] AddReactionRequest2 request);

        [Post(ReactionRootEndpoint + "/delete")]
        Task<ApiResponse<HttpContent>> DeleteReaction([Body] DeleteReactionRequest2 request);

        [Post(ReactionRootEndpoint + "/deleteall")]
        Task<ApiResponse<HttpContent>> DeleteAllReactions([Body] DeleteAllReactionsRequest2 request);

        [Post(ReactionRootEndpoint + "/list")]
        Task<ApiResponse<GetReactionsResponse2>> GetReactions([Body] GetReactionsRequest2 request);
        
        [Post(ReactionRootEndpoint + "/summary")]
        Task<ApiResponse<GetReactionCountsResponse2>> GetReactionCountsByFile([Body] GetReactionsRequest2 request);

        [Post(ReactionRootEndpoint + "/listbyidentity")]
        Task<ApiResponse<List<string>>> GetReactionsByIdentity([Body] GetReactionsByIdentityRequest2 request);
    }
}