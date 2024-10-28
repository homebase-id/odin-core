using System.Net.Http;
using System.Threading.Tasks;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.DataConversion
{
    public interface IRefitUniversalDataConversion
    {
        private const string RootPath = "/data-conversion";

        [Post(RootPath + "/prepare-introductions-release")]
        Task<ApiResponse<HttpContent>> PrepareIntroductionsRelease();
    }
}