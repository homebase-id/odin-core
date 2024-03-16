using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Services.Drives;
using Odin.Services.Drives.Reactions;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Controllers.ClientToken.Shared.Drive;
using Odin.Services.Peer.Outgoing.Drive.Reactions;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Drive
{
    public interface IUniversalDriveReactionHttpClient
    {
        private const string RootEndpoint = "/drive/files/reactions";

        [Post(RootEndpoint + "/group-add")]
        Task<ApiResponse<AddGroupReactionResponse>> AddGroupReaction([Body] AddGroupReactionRequest request);
        
        [Post(RootEndpoint + "/group-delete")]
        Task<ApiResponse<DeleteGroupReactionResponse>> DeleteGroupReaction([Body] DeleteGroupReactionRequest request);
        
        [Post(RootEndpoint + "/add")]
        Task<ApiResponse<HttpContent>> AddReaction([Body] AddReactionRequest request);

        [Post(RootEndpoint + "/delete")]
        Task<ApiResponse<HttpContent>> DeleteReaction([Body] DeleteReactionRequest request);

        [Post(RootEndpoint + "/deleteall")]
        Task<ApiResponse<HttpContent>> DeleteReactions([Body] DeleteReactionRequest request);

        [Post(RootEndpoint + "/list")]
        Task<ApiResponse<GetReactionsResponse>> GetReactions([Body] ExternalFileIdentifier file);
        
        [Post(RootEndpoint + "/summary")]
        Task<ApiResponse<GetReactionCountsResponse>> GetReactionCountsByFile([Body] GetReactionsRequest file);

        [Post(RootEndpoint + "/listbyidentity")]
        Task<ApiResponse<List<string>>> GetReactionsByIdentity([Body] GetReactionsByIdentityRequest file);
    }
}