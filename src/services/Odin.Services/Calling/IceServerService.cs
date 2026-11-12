using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Time;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Membership.Connections;
using Odin.Services.Util;

namespace Odin.Services.Calling;

/// <summary>
/// What a device puts into <c>iceServers</c> for one call: this host's STUN, and a short-lived login
/// for this host's TURN server when it has one (issue #1954, docs/turn.md).
/// </summary>
public class IceServerService(
    OdinConfiguration configuration,
    CircleNetworkService circleNetworkService,
    TurnCredentialRateLimiter rateLimiter,
    ILogger<IceServerService> logger)
{
    public async Task<IceServersResponse> GetIceServersAsync(IceServersRequest request, IOdinContext odinContext)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        OdinValidationUtils.AssertIsValidOdinId(request.Peer, out var peer);

        // The same gate as LiveRelay, which carries the call setup: an app that may send to peers
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.UseTransitWrite);
        var appId = odinContext.Caller.OdinClientContext?.AppId?.Value;
        if (!appId.HasValue)
        {
            throw new OdinClientException("ICE servers require an app context");
        }

        // Checked with TURN off too, so a client is refused the same way on every host
        var isConnected = (await circleNetworkService.GetIcrAsync(peer, odinContext, overrideHack: true)).IsConnected();
        if (!isConnected)
        {
            throw new OdinSecurityException($"{peer} is not connected", OdinClientErrorCode.NotAConnectedIdentity);
        }

        var tenant = odinContext.Tenant.DomainName;
        var iceServers = new List<IceServer>();
        var stunUrls = configuration.Stun.ClientUrls(tenant);
        if (stunUrls.Count > 0)
        {
            iceServers.Add(new IceServer { Urls = stunUrls });
        }

        var turn = configuration.Turn;
        if (!turn.Enabled)
        {
            return new IceServersResponse { IceServers = iceServers };
        }

        var now = UnixTimeUtc.Now();
        if (!rateLimiter.TryAcquire(now, out var retryAfter))
        {
            throw new OdinRetryLaterException($"{tenant} asked for too many TURN logins",
                HttpStatusCode.TooManyRequests, retryAfter);
        }

        var login = TurnCredential.Create(odinContext.Tenant, turn.SharedSecret, now,
            TimeSpan.FromSeconds(turn.CredentialLifetimeSeconds));
        iceServers.Add(new IceServer
        {
            Urls = turn.ClientUrls(tenant),
            Username = login.Username,
            Credential = login.Credential
        });

        // Never the credential itself
        logger.LogInformation("TURN login for {identity} to call {peer} from app {appId}, expires {expiresAt}",
            tenant, peer, appId.Value, login.ExpiresAt);

        return new IceServersResponse { IceServers = iceServers, ExpiresAt = login.ExpiresAt };
    }
}
