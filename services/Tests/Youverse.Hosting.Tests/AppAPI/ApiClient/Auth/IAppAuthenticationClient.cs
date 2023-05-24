using System.Net.Http;
using System.Threading.Tasks;
using Refit;
using Youverse.Hosting.Controllers.ClientToken;

namespace Youverse.Hosting.Tests.AppAPI.ApiClient.Auth
{
    public interface IAppAuthenticationClient
    {
        private const string RootPath = "/api/apps/v1/auth";
        
        [Post(RootPath + "/logout")]
        public Task<HttpContent> Logout();

        [Post(AppApiPathConstants.NotificationsV1 + "/preauth")]
        public Task<ApiResponse<HttpContent>> PreAuthWebsocket();
    }
}