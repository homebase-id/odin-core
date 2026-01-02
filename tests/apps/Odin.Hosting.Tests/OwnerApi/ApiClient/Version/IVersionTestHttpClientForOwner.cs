using System.Net.Http;
using System.Threading.Tasks;
using Odin.Services.Authentication.Owner;
using Odin.Services.Configuration;
using Refit;

namespace Odin.Hosting.Tests.OwnerApi.ApiClient.Version
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface IVersionTestHttpClientForOwner
    {
        private const string Endpoint = OwnerApiPathConstants.DataConversion;

        [Post(Endpoint + "/force-version-number")]
        Task<ApiResponse<TenantVersionInfo>> ForceVersionNumber(int version);

        [Get(Endpoint + "/data-version-info")]
        Task<ApiResponse<VersionInfoResult>> GetVersionInfo();

        [Post(Endpoint + "/force-version-upgrade")]
        Task<ApiResponse<HttpContent>> ForceVersionUpgrade();
    }
}