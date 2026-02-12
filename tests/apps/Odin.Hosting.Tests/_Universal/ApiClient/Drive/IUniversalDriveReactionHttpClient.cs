using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Hosting.Controllers.Base.Drive.GroupReactions;
using Odin.Services.Drives.Reactions;
using Odin.Services.Base;
using Odin.Services.Drives.Reactions.Redux.Group;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Drive
{
    public interface IUniversalDriveReactionHttpClient
    {
        private const string ReactionRootEndpoint = "/drive/files/group/reactions";

        [Post(ReactionRootEndpoint)]
        Task<ApiResponse<AddReactionResult>> AddReaction([Body] AddReactionRequestRedux request);

        [Delete(ReactionRootEndpoint)]
        Task<ApiResponse<DeleteReactionResult>> DeleteReaction([Body] DeleteReactionRequestRedux request);


        [Get(ReactionRootEndpoint)]
        Task<ApiResponse<GetReactionsResponse>> GetReactions([Query] GetReactionsRequestRedux request);

        [Get(ReactionRootEndpoint + "/summary")]
        Task<ApiResponse<GetReactionCountsResponse>> GetReactionCountsByFile([Query] GetReactionsRequestRedux request);

        [Get(ReactionRootEndpoint + "/by-identity")]
        Task<ApiResponse<List<string>>> GetReactionsByIdentity([Query] GetReactionsByIdentityRequestRedux request);
    }
}