using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Util;
using Odin.Services.Authorization.Acl;
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

    public async Task<IcrVerificationResult> VerifyConnection(OdinId recipient, IOdinContext odinContext, IdentityDatabase db)
    {
        // so this is a curious issue - 
        // when the odinContext.Caller and the recipient param are the same
        // there's a chance the odinContext.Caller will be only authenticated
        // because the ICR is invalid but the ICR's status will show as connected
        // 
        
        var icr = await CircleNetworkService.GetIcr(recipient, odinContext, db, overrideHack: true);

        if (odinContext.Caller.SecurityLevel == SecurityGroupType.Authenticated)
        {
            if (odinContext.Caller.OdinId == recipient)
            {
                return new IcrVerificationResult
                {
                    IsValid = false,
                    RemoteIdentityWasConnected = icr.IsConnected()
                };
            }
            //if the caller is only authenticated, there will be no ICR so verification will fail
            throw new OdinIdentityVerificationException("Cannot perform verification since caller is not connected");
        }
        

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
            var transitReadContext = OdinContextUpgrades.UseTransitRead(odinContext);
            var clientAuthToken = await ResolveClientAccessToken(recipient, transitReadContext, db, failIfNotConnected: false);

            if (null == clientAuthToken)
            {
                // icr shows as connected but we cannot get to the ICR, this means that 
                return new IcrVerificationResult
                {
                    IsValid = false,
                    RemoteIdentityWasConnected = null
                };
            }

            ApiResponse<VerifyConnectionResponse> response;
            await TryRetry.WithDelayAsync(
                odinConfiguration.Host.PeerOperationMaxAttempts,
                odinConfiguration.Host.PeerOperationDelayMs,
                CancellationToken.None,
                async () =>
                {
                    var client = clientAuthToken == null
                        ? OdinHttpClientFactory.CreateClient<ICircleNetworkVerificationClient>(recipient)
                        : OdinHttpClientFactory.CreateClientUsingAccessToken<ICircleNetworkVerificationClient>(recipient,
                            clientAuthToken.ToAuthenticationToken());

                    response = await client.VerifyConnection();

                    // Only compare if we get back a good code, so we don't kill
                    // an ICR because the remote server is not responding
                    if (response.IsSuccessStatusCode)
                    {
                        if (response.Headers.TryGetValues(HttpHeaderConstants.RemoteServerIcrIssue, out var values) && bool.Parse(values.Single()))
                        {
                            //the remote ICR is dead
                            result.RemoteIdentityWasConnected = false;
                            result.IsValid = false;
                        }
                        else
                        {
                            var vcr = response.Content;
                            result.RemoteIdentityWasConnected = vcr.IsConnected;
                            if (vcr.IsConnected)
                            {
                                result.IsValid = ByteArrayUtil.EquiByteArrayCompare(vcr.Hash, expectedHash);
                            }
                        }
                    }
                    else
                    {
                        // If we got back any other type of response, let's tell the caller
                        throw new OdinSystemException("Cannot verify connection due to remote server error");
                    }
                });
        }
        catch (OdinSecurityException ex)
        {
            //cannot get to ICR Key
            throw new OdinIdentityVerificationException("Cannot perform verification", ex);
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
    public async Task<bool> SynchronizeVerificationHash(OdinId odinId, IOdinContext odinContext, IdentityDatabase db)
    {
        odinContext.Caller.AssertHasMasterKey();

        var icr = await CircleNetworkService.GetIcr(odinId, odinContext, db);

        if (icr.Status == ConnectionStatus.Connected)
        {
            var targetIdentity = icr.OdinId;
            var randomCode = ByteArrayUtil.GetRandomCryptoGuid();

            var success = await UpdateRemoteIdentityVerificationCode(targetIdentity, randomCode, odinContext, db);

            if (success)
            {
                return await CircleNetworkService.UpdateVerificationHash(targetIdentity, randomCode, odinContext, db);
            }
        }

        return false;
    }

    public async Task SynchronizeVerificationHashFromRemote(SharedSecretEncryptedPayload payload, IOdinContext odinContext, IdentityDatabase db)
    {
        odinContext.Caller.AssertCallerIsConnected();

        var bytes = payload.Decrypt(odinContext.PermissionsContext.SharedSecretKey);
        var request = OdinSystemSerializer.Deserialize<UpdateVerificationHashRequest>(bytes.ToStringFromUtf8Bytes());
        await CircleNetworkService.UpdateVerificationHash(odinContext.GetCallerOdinIdOrFail(), request.RandomCode, odinContext, db);
    }

    private async Task<bool> UpdateRemoteIdentityVerificationCode(OdinId recipient, Guid randomCode, IOdinContext odinContext, IdentityDatabase db)
    {
        var request = new UpdateVerificationHashRequest()
        {
            RandomCode = randomCode
        };

        bool success = false;
        try
        {
            var clientAuthToken = await ResolveClientAccessToken(recipient, odinContext, db, false);

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