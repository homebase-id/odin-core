using System.Threading.Tasks;
using Refit;
using Youverse.Core.Services.Base;
using Youverse.Hosting.Controllers.ClientToken;

namespace Youverse.Hosting.Tests.AppAPI.ApiClient.Security
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface ITestSecurityContextAppClient
    {
        [Get(AppApiPathConstants.SecurityV1 + "/context")]
        Task<ApiResponse<RedactedOdinContext>> GetDotYouContext();
    }
}