using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Services.Optimization.Cdn;
using Odin.Hosting.Controllers.OwnerToken;
using Odin.Hosting.Controllers.OwnerToken.Cdn;
using Refit;

namespace Odin.Hosting.Tests.OwnerApi.Optimization.Cdn
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface IStaticFileTestHttpClientForOwner
    {
        private const string RootEndpoint = OwnerApiPathConstants.CdnV1;

        [Post(RootEndpoint + "/publish")]
        Task<ApiResponse<StaticFilePublishResult>> Publish([Body] PublishStaticFileRequest request);
        
        [Post(RootEndpoint + "/profileimage")]
        Task<ApiResponse<HttpContent>> PublishPublicProfileImage([Body] PublishPublicProfileImageRequest request);
        
        [Post(RootEndpoint + "/profilecard")]
        Task<ApiResponse<HttpContent>> PublishPublicProfileCard([Body] PublishPublicProfileCardRequest request);

        [Get("/cdn/{filename}")]
        Task<ApiResponse<HttpContent>> GetStaticFile(string filename);
        
        [Get("/pub/image")]
        Task<ApiResponse<HttpContent>> GetPublicProfileImage();
        
        [Get("/pub/profile")]
        Task<ApiResponse<HttpContent>> GetPublicProfileCard();
    }
}