using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Hosting.Controllers.OwnerToken.Security;
using Odin.Services.Authentication.Owner;
using Odin.Services.Base;
using Odin.Services.Security;
using Odin.Services.Security.PasswordRecovery.RecoveryPhrase;
using Odin.Services.Security.PasswordRecovery.Shamir;
using Odin.Services.Security.PasswordRecovery.Shamir.ShardRequestApproval;
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
        
        [Post(OwnerApiPathConstants.SecurityV1 + "/request-recovery-key")]
        Task<ApiResponse<RequestRecoveryKeyResult>> RequestRecoveryKey();

        [Post(OwnerApiPathConstants.SecurityV1 + "/resetpasswd")]
        Task<ApiResponse<HttpContent>> ResetPassword(ResetPasswordRequest request);

        [Post(OwnerApiPathConstants.SecurityRecoveryV1 + "/configure-shards")]
        Task<ApiResponse<HttpContent>> ConfigureShards(ConfigureShardsRequest request);

        [Post(OwnerApiPathConstants.SecurityRecoveryV1 + "/verify-remote-shards")]
        Task<ApiResponse<RemoteShardVerificationResult>> VerifyShards();

        [Post(OwnerApiPathConstants.SecurityRecoveryV1 + "/initiate-recovery-mode")]
        Task<ApiResponse<HttpContent>> InitiateRecoveryMode();

        [Get(OwnerApiPathConstants.SecurityRecoveryV1 + "/verify-enter")]
        Task<ApiResponse<HttpContent>> VerifyEnterRecoveryMode(string id);
        
        [Post(OwnerApiPathConstants.SecurityRecoveryV1 + "/exit-recovery-mode")]
        Task<ApiResponse<HttpContent>> ExitRecoveryMode();
        
        [Get(OwnerApiPathConstants.SecurityRecoveryV1 + "/verify-exit")]
        Task<ApiResponse<HttpContent>> VerifyExitRecoveryMode(string id);

        [Post(OwnerApiPathConstants.SecurityRecoveryV1 + "/finalize")]
        Task<ApiResponse<HttpContent>> FinalizeRecovery(FinalRecoveryRequest request);

        [Post(OwnerApiPathConstants.SecurityRecoveryV1 + "/reject-shard-request")]
        Task<ApiResponse<HttpContent>> RejectShardRequest(RejectShardRequest request);
       
        [Post(OwnerApiPathConstants.SecurityRecoveryV1 + "/approve-shard-request")]
        Task<ApiResponse<HttpContent>> ApproveShardRequest(ApproveShardRequest request);
        
        [Get(OwnerApiPathConstants.SecurityRecoveryV1 + "/shard-request-list")]
        Task<ApiResponse<List<ShardApprovalRequest>>> GetShardRequestList();
        
        [Get(OwnerApiPathConstants.SecurityRecoveryV1 + "/config")]
        Task<ApiResponse<DealerShardConfig>> GetShardConfig();
        
        [Get(OwnerApiPathConstants.SecurityRecoveryV1 + "/status")]
        Task<ApiResponse<ShamirRecoveryStatusRedacted>> GetShamirRecoverStatus();
    }
}