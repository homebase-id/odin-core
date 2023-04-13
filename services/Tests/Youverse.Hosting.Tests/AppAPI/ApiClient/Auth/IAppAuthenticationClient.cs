using System.Net.Http;
using System.Threading.Tasks;
using Refit;

namespace Youverse.Hosting.Tests.AppAPI.ApiClient.Auth
{
    public interface IAppAuthenticationClient
    {
        private const string RootPath = "/api/apps/v1/auth";

        [Post(RootPath + "/logout")]
        public Task<HttpContent> Logout();
    }
}