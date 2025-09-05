using System.Net.Http;
using System.Threading.Tasks;
using Odin.Hosting.Controllers.OwnerToken.Security;
using Odin.Services.Authentication.Owner;
using Odin.Services.Base;
using Odin.Services.ShamiraPasswordRecovery;
using Refit;

namespace Odin.Hosting.Tests.OwnerApi.ApiClient.Security
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface ITestSecurityContextOwnerClient
    {
        [Get(OwnerApiPathConstants.SecurityV1 + "/context")]
        Task<ApiResponse<RedactedOdinContext>> GetDotYouContext();

        [Get(OwnerApiPathConstants.SecurityV1 + "/recovery-key")]
        Task<ApiResponse<DecryptedRecoveryKey>> GetAccountRecoveryKey();

        [Post(OwnerApiPathConstants.SecurityV1 + "/resetpasswd")]
        Task<ApiResponse<HttpContent>> ResetPassword(ResetPasswordRequest request);

        [Post(OwnerApiPathConstants.SecurityRecoveryV1 + "/configure-shards")]
        Task<ApiResponse<HttpContent>> ConfigureShards(ConfigureShardsRequest request);

        [Post(OwnerApiPathConstants.SecurityRecoveryV1 + "/verify-remote-shards")]
        Task<ApiResponse<RemoteShardVerificationResult>> VerifyShards();
    }
}