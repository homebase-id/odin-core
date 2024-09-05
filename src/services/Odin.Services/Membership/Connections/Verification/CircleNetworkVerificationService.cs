using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite;
using Odin.Core.Util;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Membership.Connections.Requests;
using Odin.Services.Peer;
using Refit;

namespace Odin.Services.Membership.Connections.Verification;

/// <summary>
/// Enables us to update verification hashes across identities
/// </summary>
public class CircleNetworkVerificationService(
    OdinConfiguration odinConfiguration,
    CircleNetworkService cns,
    IOdinHttpClientFactory odinHttpClientFactory,
    FileSystemResolver fileSystemResolver)
    : PeerServiceBase(odinHttpClientFactory, cns, fileSystemResolver)
{
    // private readonly ILogger<CircleNetworkVerificationService> _logger = logger;

    public async Task<IcrVerificationResult> VerifyConnection(OdinId recipient, IOdinContext odinContext, DatabaseConnection cn)
    {
        var icr = await CircleNetworkService.GetIcr(recipient, odinContext, cn, overrideHack: true);

        if (!icr.IsConnected())
        {
            return new IcrVerificationResult
            {
                IsValid = false,
                RemoteIdentityWasConnected = null
            };
        }

        var expectedHash = icr!.VerificationHash;
        if (expectedHash?.Length == 0)
        {
            throw new OdinClientException("Missing verification hash", OdinClientErrorCode.MissingVerificationHash);
        }

        var result = new IcrVerificationResult();
        try
        {
            var clientAuthToken = await ResolveClientAccessToken(recipient, odinContext, cn, false);

            ApiResponse<VerifyConnectionResponse> response;
            await TryRetry.WithDelayAsync(
                odinConfiguration.Host.PeerOperationMaxAttempts,
                odinConfiguration.Host.PeerOperationDelayMs,
                CancellationToken.None,
                async () =>
                {
                    var client = OdinHttpClientFactory.CreateClientUsingAccessToken<ICircleNetworkVerificationClient>(recipient,
                        clientAuthToken.ToAuthenticationToken());

                    response = await client.VerifyConnection();
                    if (response.IsSuccessStatusCode)
                    {
                        var vcr = response.Content;

                        //only compare if we get back a good code, so we don't kill
                        //an ICR because the remote server is not responding
                        result.RemoteIdentityWasConnected = vcr.IsConnected;
                        if (vcr.IsConnected)
                        {
                            result.IsValid = ByteArrayUtil.EquiByteArrayCompare(vcr.Hash, expectedHash);
                        }
                    }
                    else
                    {
                        // If we got back any other type of response, let's tell the caller
                        throw new OdinSystemException("Cannot verify connection due to remote server error");
                    }
                });
        }
        catch (TryRetryException e)
        {
            throw e.InnerException ?? e;
        }

        return result;
    }
    
    /// <summary>
    /// Sends a new randomCode to a connected identity to synchronize verification codes
    /// </summary>
    public async Task<bool> SynchronizeVerificationHash(OdinId odinId, IOdinContext odinContext, DatabaseConnection cn)
    {
        odinContext.Caller.AssertHasMasterKey();

        var icr = await CircleNetworkService.GetIcr(odinId, odinContext, cn);

        if (icr.Status == ConnectionStatus.Connected)
        {
            var targetIdentity = icr.OdinId;
            var randomCode = ByteArrayUtil.GetRandomCryptoGuid();

            var success = await UpdateRemoteIdentityVerificationCode(targetIdentity, randomCode, odinContext, cn);

            if (success)
            {
                return await CircleNetworkService.UpdateVerificationHash(targetIdentity, randomCode, odinContext, cn);
            }
        }

        return false;
    }

    public async Task SynchronizeVerificationHashFromRemote(SharedSecretEncryptedPayload payload, IOdinContext odinContext, DatabaseConnection cn)
    {
        odinContext.Caller.AssertCallerIsConnected();

        var bytes = payload.Decrypt(odinContext.PermissionsContext.SharedSecretKey);
        var request = OdinSystemSerializer.Deserialize<UpdateVerificationHashRequest>(bytes.ToStringFromUtf8Bytes());
        await CircleNetworkService.UpdateVerificationHash(odinContext.GetCallerOdinIdOrFail(), request.RandomCode, odinContext, cn);
    }

    private async Task<bool> UpdateRemoteIdentityVerificationCode(OdinId recipient, Guid randomCode, IOdinContext odinContext, DatabaseConnection cn)
    {
        var request = new UpdateVerificationHashRequest()
        {
            RandomCode = randomCode
        };

        bool success = false;
        try
        {
            var clientAuthToken = await ResolveClientAccessToken(recipient, odinContext, cn, false);

            ApiResponse<HttpContent> response;
            await TryRetry.WithDelayAsync(
                odinConfiguration.Host.PeerOperationMaxAttempts,
                odinConfiguration.Host.PeerOperationDelayMs,
                CancellationToken.None,
                async () =>
                {
                    var json = OdinSystemSerializer.Serialize(request);
                    var encryptedPayload = SharedSecretEncryptedPayload.Encrypt(json.ToUtf8ByteArray(), clientAuthToken.SharedSecret);
                    var client = OdinHttpClientFactory.CreateClientUsingAccessToken<ICircleNetworkVerificationClient>(recipient,
                        clientAuthToken.ToAuthenticationToken());

                    response = await client.UpdateRemoteVerificationHash(encryptedPayload);
                    success = response.IsSuccessStatusCode;
                });
        }
        catch (TryRetryException e)
        {
            throw e.InnerException ?? e;
        }

        return success;
    }
}