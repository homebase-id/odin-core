using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Optimization.Cdn;
using Odin.Hosting.Controllers.Base.Cdn;
using Odin.Hosting.Controllers.OwnerToken.Cdn;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Drive
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface IUniversalPublicStaticFileHttpClientApi
    {

        [Get("/cdn/{filename}")]
        Task<ApiResponse<HttpContent>> GetStaticFile(string filename);
        
        [Get("/pub/image")]
        Task<ApiResponse<HttpContent>> GetPublicProfileImage();
        
        [Get("/pub/profile")]
        Task<ApiResponse<HttpContent>> GetPublicProfileCard();
    }
}