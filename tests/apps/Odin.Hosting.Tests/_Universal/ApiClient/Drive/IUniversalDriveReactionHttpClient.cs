using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Services.Drives.Reactions;
using Odin.Hosting.Controllers.Base.Drive.ReactionsRedux;
using Odin.Services.Base;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Drive
{
    public interface IUniversalDriveReactionHttpClient
    {
        private const string ReactionRootEndpoint = "/drive/files/group/reactions";

        [Post(ReactionRootEndpoint)]
        Task<ApiResponse<HttpContent>> AddReaction([Body] AddReactionRequestRedux request);

        [Delete(ReactionRootEndpoint)]
        Task<ApiResponse<HttpContent>> DeleteReaction([Body] DeleteReactionRequestRedux request);

        [Get(ReactionRootEndpoint)]
        Task<ApiResponse<GetReactionsResponse>> GetReactions([Query] FileIdentifier file);
        
        [Get(ReactionRootEndpoint + "/summary")]
        Task<ApiResponse<GetReactionCountsResponse>> GetReactionCountsByFile([Query] GetReactionsRequestRedux file);

        [Get(ReactionRootEndpoint+ "/by-identity")]
        Task<ApiResponse<List<string>>> GetReactionsByIdentity([Query] GetReactionsByIdentityRequestRedux request);
    }
}