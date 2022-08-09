using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Refit;
using Youverse.Core.Services.Optimization.Cdn;
using Youverse.Hosting.Controllers;
using Youverse.Hosting.Controllers.OwnerToken;
using Youverse.Hosting.Controllers.OwnerToken.Cdn;

namespace Youverse.Hosting.Tests.OwnerApi.Optimization.Cdn
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface IStaticFileTestHttpClientForOwner
    {
        private const string RootEndpoint = OwnerApiPathConstants.CdnV1;

        [Post(RootEndpoint + "/publish")]
        Task<ApiResponse<StaticFilePublishResult>> Publish([Body] PublishStaticFileRequest request);

        [Get("/cdn/{filename}")]
        Task<ApiResponse<HttpContent>> GetStaticFile(string filename);
    }
}