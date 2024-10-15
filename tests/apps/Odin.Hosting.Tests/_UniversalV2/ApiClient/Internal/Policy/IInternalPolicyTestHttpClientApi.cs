using System.Net.Http;
using System.Threading.Tasks;
using Odin.Hosting.Controllers.APIv2;
using Refit;

namespace Odin.Hosting.Tests._UniversalV2.ApiClient.Internal.Policy
{
    public interface IInternalPolicyTestHttpClientApi
    {
        private const string Root = "/";

        [Get(Root + ApiV2PathConstants.PolicyTests.Get)]
        Task<ApiResponse<HttpContent>> GetTest();
    }
}