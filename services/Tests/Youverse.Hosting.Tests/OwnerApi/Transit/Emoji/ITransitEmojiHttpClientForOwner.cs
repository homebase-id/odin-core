using System.Net.Http;
using System.Threading.Tasks;
using Refit;
using Youverse.Core.Services.Drives.Reactions;
using Youverse.Core.Services.Transit.SendingHost;
using Youverse.Hosting.Controllers.OwnerToken;

namespace Youverse.Hosting.Tests.OwnerApi.Transit.Emoji
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface ITransitEmojiHttpClientForOwner
    {
        private const string RootEndpoint = OwnerApiPathConstants.TransitEmojiV1;

        [Post(RootEndpoint + "/add")]
        Task<ApiResponse<HttpContent>> AddReaction([Body] TransitAddReactionRequest request);

        [Post(RootEndpoint + "/list")]
        Task<ApiResponse<GetReactionsResponse>> GetAllReactions([Body] TransitGetReactionsRequest file);

        [Post(RootEndpoint + "/delete")]
        Task<ApiResponse<HttpContent>> DeleteEmojiReaction([Body] TransitDeleteReactionRequest file);

        [Post(RootEndpoint + "/deleteall")]
        Task<ApiResponse<HttpContent>> DeleteAllReactionsOnFile([Body] TransitDeleteReactionRequest file);

        [Post(RootEndpoint + "/summary")]
        Task<ApiResponse<GetReactionCountsResponse>> GetReactionCountsByFile([Body] TransitDeleteReactionRequest file);

        [Post(RootEndpoint + "/listbyidentity")]
        Task<ApiResponse<HttpContent>> GetReactionsByIdentity([Body] TransitGetReactionsByIdentityRequest file);
    }
}