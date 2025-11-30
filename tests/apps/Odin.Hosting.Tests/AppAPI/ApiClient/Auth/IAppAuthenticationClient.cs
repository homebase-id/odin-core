using System.Net.Http;
using System.Threading.Tasks;
using Odin.Hosting.Controllers.ClientToken;
using Odin.Hosting.Controllers.ClientToken.App;
using Refit;

namespace Odin.Hosting.Tests.AppAPI.ApiClient.Auth
{
    public interface IAppAuthenticationClient
    {
        private const string RootPath = AppApiPathConstantsV1.AuthV1;

        [Post(RootPath + "/logout")]
        public Task<HttpContent> Logout();

        [Post(AppApiPathConstantsV1.NotificationsV1 + "/preauth")]
        public Task<ApiResponse<HttpContent>> PreAuthWebsocket();
    }
}