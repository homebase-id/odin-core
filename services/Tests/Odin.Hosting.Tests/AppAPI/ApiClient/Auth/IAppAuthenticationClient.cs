using System.Net.Http;
using System.Threading.Tasks;
using Odin.Hosting.Controllers.ClientToken;
using Odin.Hosting.Controllers.ClientToken.App;
using Refit;

namespace Odin.Hosting.Tests.AppAPI.ApiClient.Auth
{
    public interface IAppAuthenticationClient
    {
        private const string RootPath = AppApiPathConstants.AuthV1;

        [Post(RootPath + "/logout")]
        public Task<HttpContent> Logout();

        [Post(AppApiPathConstants.NotificationsV1 + "/preauth")]
        public Task<ApiResponse<HttpContent>> PreAuthWebsocket();
    }
}