using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Base;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Refit;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Logging.Statistics.Serilog;
using Odin.Hosting.Controllers.OwnerToken.Security;
using Odin.Services.Security;
using Odin.Services.Security.PasswordRecovery.RecoveryPhrase;
using Odin.Services.Security.PasswordRecovery.Shamir;
using Odin.Services.Security.PasswordRecovery.Shamir.ShardRequestApproval;
using Serilog.Events;

namespace Odin.Hosting.Tests.OwnerApi.ApiClient.Security;

public class SecurityApiClient(OwnerApiTestUtils ownerApi, TestIdentity identity)
{
    public async Task<RedactedOdinContext> GetSecurityContext()
    {
        var client = ownerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<ITestSecurityContextOwnerClient>(client, ownerSharedSecret);
            var apiResponse = await svc.GetDotYouContext();
            return apiResponse.Content;
        }
    }

    public async Task<ApiResponse<RecoveryKeyResult>> GetAccountRecoveryKey()
    {
        var client = ownerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<ITestSecurityContextOwnerClient>(client, ownerSharedSecret);
            var apiResponse = await svc.GetAccountRecoveryKey();
            return apiResponse;
        }
    }

    public async Task<ApiResponse<RequestRecoveryKeyResult>> RequestRecoveryKey()
    {
        var client = ownerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<ITestSecurityContextOwnerClient>(client, ownerSharedSecret);
            var apiResponse = await svc.RequestRecoveryKey();
            return apiResponse;
        }
    }
    
    public async Task<ApiResponse<HttpContent>> ResetPassword(string currentPassword, string newPassword)
    {
        using var authClient = ownerApi.CreateAnonymousClient(identity.OdinId);
        var clientEccFullKey = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);

        var request = new ResetPasswordRequest()
        {
            CurrentAuthenticationPasswordReply =
                await ownerApi.CalculateAuthenticationPasswordReply(authClient, currentPassword, clientEccFullKey),
            NewPasswordReply = await ownerApi.CalculatePasswordReply(authClient, newPassword, clientEccFullKey)
        };

        var client = ownerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<ITestSecurityContextOwnerClient>(client, ownerSharedSecret);
            var apiResponse = await svc.ResetPassword(request);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<HttpContent>> ResetPasswordUsingRecoveryKey(string recoveryKey, string newPassword)
    {
        return await ownerApi.ResetPasswordUsingRecoveryKey(identity.OdinId, recoveryKey, newPassword);
    }

    public async Task<ApiResponse<HttpContent>> ConfigureShards(ConfigureShardsRequest request)
    {
        var client = ownerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<ITestSecurityContextOwnerClient>(client, ownerSharedSecret);
            var apiResponse = await svc.ConfigureShards(request);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<RemoteShardVerificationResult>> VerifyShards()
    {
        var client = ownerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<ITestSecurityContextOwnerClient>(client, ownerSharedSecret);
            var apiResponse = await svc.VerifyShards();
            return apiResponse;
        }
    }

    public async Task<ApiResponse<HttpContent>> EnterRecoveryMode()
    {
        var client = ownerApi.CreateAnonymousClient(identity.OdinId);
        {
            var svc = RestService.For<ITestSecurityContextOwnerClient>(client);
            var apiResponse = await svc.InitiateRecoveryMode();
            return apiResponse;
        }
    }

    public async Task<ApiResponse<HttpContent>> VerifyEnterRecoveryMode(string nonceId)
    {
        var client = ownerApi.CreateAnonymousClient(identity.OdinId);
        {
            var svc = RestService.For<ITestSecurityContextOwnerClient>(client);
            var apiResponse = await svc.VerifyEnterRecoveryMode(nonceId);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<HttpContent>> ExitRecoveryMode()
    {
        var client = ownerApi.CreateAnonymousClient(identity.OdinId);
        {
            var svc = RestService.For<ITestSecurityContextOwnerClient>(client);
            var apiResponse = await svc.ExitRecoveryMode();
            return apiResponse;
        }
    }

    public async Task<ApiResponse<HttpContent>> VerifyExitRecoveryMode(string nonceId)
    {
        var client = ownerApi.CreateAnonymousClient(identity.OdinId);
        {
            var svc = RestService.For<ITestSecurityContextOwnerClient>(client);
            var apiResponse = await svc.VerifyExitRecoveryMode(nonceId);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<HttpContent>> FinalizeRecovery(FinalRecoveryRequest request)
    {
        var client = ownerApi.CreateAnonymousClient(identity.OdinId);
        {
            var svc = RestService.For<ITestSecurityContextOwnerClient>(client);
            var apiResponse = await svc.FinalizeRecovery(request);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<List<ShardApprovalRequest>>> GetShardRequestList()
    {
        var client = ownerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<ITestSecurityContextOwnerClient>(client, ownerSharedSecret);
            var apiResponse = await svc.GetShardRequestList();
            return apiResponse;
        }
    }

    public async Task<ApiResponse<HttpContent>> ApproveShardRequest(ApproveShardRequest request)
    {
        var client = ownerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<ITestSecurityContextOwnerClient>(client, ownerSharedSecret);
            var apiResponse = await svc.ApproveShardRequest(request);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<HttpContent>> RejectShardRequest(RejectShardRequest request)
    {
        var client = ownerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<ITestSecurityContextOwnerClient>(client, ownerSharedSecret);
            var apiResponse = await svc.RejectShardRequest(request);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<DealerShardConfig>> GetDealerShardConfig()
    {
        var client = ownerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<ITestSecurityContextOwnerClient>(client, ownerSharedSecret);
            var apiResponse = await svc.GetShardConfig();
            return apiResponse;
        }
    }

    public async Task<ApiResponse<ShamirRecoveryStatusRedacted>> GetShamirRecoveryStatus()
    {
        var client = ownerApi.CreateAnonymousClient(identity.OdinId);
        {
            var svc = RestService.For<ITestSecurityContextOwnerClient>(client);
            var apiResponse = await svc.GetShamirRecoverStatus();
            return apiResponse;
        }
    }

    public async Task<ShamirRecoveryStatusRedacted> WaitForShamirStatus(ShamirRecoveryState expectedState, TimeSpan? maxWaitTime = null)
    {
        var maxWait = maxWaitTime ?? TimeSpan.FromSeconds(40);

        var sw = Stopwatch.StartNew();
        while (true)
        {
            var recoveryStatusResponse = await GetShamirRecoveryStatus();
            var recoveryStatus = recoveryStatusResponse.Content;
            if (recoveryStatusResponse.IsSuccessStatusCode && null != recoveryStatus)
            {
                if (recoveryStatus.State == expectedState)
                {
                    return recoveryStatus;
                }
            }

            if (sw.Elapsed > maxWait)
            {
                throw new TimeoutException($"Failed waiting for expected state {expectedState}");
            }

            await Task.Delay(100);
        }
    }
}