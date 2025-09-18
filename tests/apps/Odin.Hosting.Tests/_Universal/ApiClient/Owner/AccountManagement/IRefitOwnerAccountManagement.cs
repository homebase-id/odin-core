using System.Net.Http;
using System.Threading.Tasks;
using Odin.Services.Authentication.Owner;
using Odin.Services.Base;
using Odin.Services.Security;
using Odin.Services.Security.PasswordRecovery.RecoveryPhrase;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Owner.AccountManagement
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface IRefitOwnerAccountManagement
    {
        [Get(OwnerApiPathConstants.SecurityV1 + "/context")]
        Task<ApiResponse<RedactedOdinContext>> GetDotYouContext();
        
        [Get(OwnerApiPathConstants.SecurityV1 + "/recovery-key")]
        Task<ApiResponse<DecryptedRecoveryKey>> GetAccountRecoveryKey();

        [Post(OwnerApiPathConstants.SecurityV1 + "/resetpasswd")]
        Task<ApiResponse<HttpContent>> ResetPassword(ResetPasswordRequest request);

        [Post(OwnerApiPathConstants.SecurityV1 + "/delete-account")]
        Task<ApiResponse<DeleteAccountResponse>> DeleteAccount(DeleteAccountRequest request);
        
        [Post(OwnerApiPathConstants.SecurityV1 + "/undelete-account")]
        Task<ApiResponse<DeleteAccountResponse>> UndeleteAccount(DeleteAccountRequest request);

        [Get(OwnerApiPathConstants.SecurityV1 + "/account-status")]
        Task<ApiResponse<AccountStatusResponse>> GetAccountStatus();
    }
}