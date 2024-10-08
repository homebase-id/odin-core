﻿using System.Net.Http;
using System.Threading.Tasks;
using Odin.Services.Authentication.Owner;
using Odin.Services.Base;
using Odin.Hosting.Controllers.OwnerToken;
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

    }
}