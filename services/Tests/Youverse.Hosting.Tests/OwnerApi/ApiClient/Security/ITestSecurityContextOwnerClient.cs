using System.Threading.Tasks;
using Refit;
using Youverse.Core.Services.Base;
using Youverse.Hosting.Controllers.OwnerToken;

namespace Youverse.Hosting.Tests.OwnerApi.ApiClient.Security
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface ITestSecurityContextOwnerClient
    {
        [Get(OwnerApiPathConstants.SecurityV1 + "/context")]
        Task<ApiResponse<RedactedOdinContext>> GetDotYouContext();
    }
}