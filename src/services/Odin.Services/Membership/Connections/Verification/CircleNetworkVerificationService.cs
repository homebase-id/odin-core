using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Util;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
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
    FileSystemResolver fileSystemResolver,
    ILogger<CircleNetworkVerificationService> logger)
    : PeerServiceBase(odinHttpClientFactory, cns, fileSystemResolver, odinConfiguration)
{
    // private readonly ILogger<CircleNetworkVerificationService> _logger = logger;

    public async Task<IcrVerificationResult> VerifyConnectionAsync(OdinId recipient, CancellationToken cancellationToken,
        IOdinContext odinContext)
    {
        // so this is a curious issue - 
        // when the odinContext.Caller and the recipient param are the same
        // there's a chance the odinContext.Caller will be only authenticated
        // because the ICR is invalid but the ICR's status will show as connected
        // 

        var icr = await CircleNetworkService.GetIcrAsync(recipient, odinContext, overrideHack: true);

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
        if (expectedHash.IsNullOrEmpty())
        {
            throw new OdinClientException("Missing verification hash", OdinClientErrorCode.MissingVerificationHash);
        }

        var result = new IcrVerificationResult();
        try
        {
            var transitReadContext = OdinContextUpgrades.UsePermissions(odinContext, PermissionKeys.UseTransitRead);
            var clientAuthToken = await ResolveClientAccessTokenAsync(recipient, transitReadContext, failIfNotConnected: false);

            if (null == clientAuthToken)
            {
                // icr shows as connected, however we cannot get to the ICR;
                // this means that caller might not have the ICR key
                return new IcrVerificationResult
                {
                    IsValid = false,
                    RemoteIdentityWasConnected = null
                };
            }

            var executionResult = await ExecuteRequestAsync(VerifyPeerConnection(clientAuthToken), cancellationToken);

            // Only compare if we get back a good code, so we don't kill
            // an ICR because the remote server is not responding
            if (executionResult.Response.IsSuccessStatusCode)
            {
                var vcr = executionResult.Response.Content;
                result.RemoteIdentityWasConnected = vcr.IsConnected;

                logger.LogDebug("Comparing verification-hash: remote identity has hash {remoteYesNo} | " +
                                "local identity has has: {localYesNo}",
                    vcr.Hash?.Length > 0,
                    expectedHash?.Length > 0);

                if (result.RemoteIdentityWasConnected.GetValueOrDefault())
                {
                    result.IsValid = ByteArrayUtil.EquiByteArrayCompare(vcr.Hash, expectedHash);
                }
            }
            else
            {
                switch (executionResult.IssueType)
                {
                    case PeerRequestIssueType.Forbidden:
                    case PeerRequestIssueType.ForbiddenWithInvalidRemoteIcr:
                        result.RemoteIdentityWasConnected = false;
                        result.IsValid = false;
                        break;

                    case PeerRequestIssueType.DnsResolutionFailure:
                    case PeerRequestIssueType.HttpRequestFailed:
                    case PeerRequestIssueType.OperationCancelled:

                    case PeerRequestIssueType.ServiceUnavailable:
                    case PeerRequestIssueType.InternalServerError:
                        throw new OdinSystemException("Cannot verify connection.");

                    case PeerRequestIssueType.Unhandled:
                        throw new OdinSystemException("Cannot verify connection. Issue type unhandled.");
                }

                return result;
            }
        }
        catch (OdinSecurityException ex)
        {
            //cannot get to ICR Key
            throw new OdinIdentityVerificationException("Cannot perform verification", ex);
        }

        return result;

        async Task<ApiResponse<VerifyConnectionResponse>> VerifyPeerConnection(ClientAccessToken clientAuthToken)
        {
            var client = OdinHttpClientFactory.CreateClientUsingAccessToken<ICircleNetworkPeerConnectionsClient>(recipient,
                clientAuthToken.ToAuthenticationToken());

            ApiResponse<VerifyConnectionResponse> response = await client.VerifyConnection();
            return response;
        }
    }

    public async Task SyncHashOnAllConnectedIdentities(IOdinContext odinContext, CancellationToken cancellationToken)
    {
        odinContext.Caller.AssertHasMasterKey();

        var allIdentities = await CircleNetworkService.GetConnectedIdentitiesAsync(int.MaxValue, 0, odinContext);

        //TODO CONNECTIONS
        // await db.CreateCommitUnitOfWorkAsync(async () =>
        {
            foreach (var identity in allIdentities.Results)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (identity.VerificationHash.IsNullOrEmpty())
                {
                    try
                    {
                        var success = await SynchronizeVerificationHashAsync(identity.OdinId, cancellationToken, odinContext);
                        logger.LogDebug("EnsureVerificationHash for {odinId}.  Succeeded: {success}", identity.OdinId, success);
                    }
                    catch (Exception e)
                    {
                        logger.LogDebug(e, "EnsureVerificationHash for {odinId}.  Failed", identity.OdinId);
                    }
                }
            }
        }
        //);
    }

    /// <summary>
    /// Sends a new randomCode to a connected identity to synchronize verification codes
    /// </summary>
    public async Task<bool> SynchronizeVerificationHashAsync(OdinId odinId, CancellationToken cancellationToken, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        var icr = await CircleNetworkService.GetIcrAsync(odinId, odinContext);

        if (icr.Status == ConnectionStatus.Connected)
        {
            logger.LogDebug("Syncing verification hash for connected [{identity}]", odinId);

            var targetIdentity = icr.OdinId;
            var randomCode = ByteArrayUtil.GetRandomCryptoGuid();

            var success = await UpdateRemoteIdentityVerificationCodeAsync(targetIdentity, randomCode, cancellationToken, odinContext);

            if (success)
            {
                var cat = icr.EncryptedClientAccessToken.Decrypt(odinContext.PermissionsContext.GetIcrKey());
                return await CircleNetworkService.UpdateVerificationHashAsync(targetIdentity, randomCode, cat.SharedSecret, odinContext);
            }
        }

        return false;
    }

    public async Task SynchronizeVerificationHashFromRemoteAsync(SharedSecretEncryptedPayload payload, IOdinContext odinContext)
    {
        odinContext.Caller.AssertCallerIsConnected();

        var bytes = payload.Decrypt(odinContext.PermissionsContext.SharedSecretKey);
        var request = OdinSystemSerializer.Deserialize<UpdateVerificationHashRequest>(bytes.ToStringFromUtf8Bytes());
        await CircleNetworkService.UpdateVerificationHashAsync(odinContext.GetCallerOdinIdOrFail(), request.RandomCode,
            odinContext.PermissionsContext.SharedSecretKey, odinContext);
    }

    private async Task<bool> UpdateRemoteIdentityVerificationCodeAsync(OdinId recipient, Guid randomCode,
        CancellationToken cancellationToken, IOdinContext odinContext)
    {
        async Task<ApiResponse<HttpContent>> UpdatePeer()
        {
            var clientAuthToken = await ResolveClientAccessTokenAsync(recipient, odinContext, false);

            var json = OdinSystemSerializer.Serialize(new UpdateVerificationHashRequest()
            {
                RandomCode = randomCode
            });

            var encryptedPayload = SharedSecretEncryptedPayload.Encrypt(json.ToUtf8ByteArray(), clientAuthToken.SharedSecret);
            var client = OdinHttpClientFactory.CreateClientUsingAccessToken<ICircleNetworkPeerConnectionsClient>(recipient,
                clientAuthToken.ToAuthenticationToken());

            return await client.UpdateRemoteVerificationHash(encryptedPayload);
        }

        try
        {
            var executionResult = await ExecuteRequestAsync(UpdatePeer(), cancellationToken);

            switch (executionResult.IssueType)
            {
                case PeerRequestIssueType.DnsResolutionFailure:
                    return false;

                case PeerRequestIssueType.OperationCancelled:
                    return false;

                case PeerRequestIssueType.HttpRequestFailed:
                    return false;
                
                case PeerRequestIssueType.ForbiddenWithInvalidRemoteIcr:
                    await CircleNetworkService.DisconnectAsync(recipient, odinContext);
                    return false;
                
                case PeerRequestIssueType.Forbidden:
                    throw new OdinSecurityException("Remote server returned 403");

                case PeerRequestIssueType.ServiceUnavailable:
                    throw new OdinClientException("Remote server returned 503", OdinClientErrorCode.RemoteServerReturnedUnavailable);

                case PeerRequestIssueType.InternalServerError:
                    throw new OdinClientException("Remote server returned 500",
                        OdinClientErrorCode.RemoteServerReturnedInternalServerError);

                case PeerRequestIssueType.Unhandled:
                    throw new OdinSystemException($"Unhandled peer error response: {executionResult.Response.StatusCode}");
            }

            return executionResult.Response.IsSuccessStatusCode;
        }
        catch (TryRetryException e)
        {
            throw e.InnerException!;
        }
    }
}