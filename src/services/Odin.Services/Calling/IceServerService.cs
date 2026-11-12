using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.LiveRelay;
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

        // LiveRelay carries the call setup, so whoever may relay may get the servers
        var appId = LiveRelayService.AssertMayRelay(odinContext);

        // Checked with TURN off too, so a client is refused the same way on every host
        var isConnected = (await circleNetworkService.GetIcrAsync(peer, odinContext, overrideHack: true, tryUpgradeEncryption: false)).IsConnected();
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

        // Over the limit the device still gets STUN: a call that cannot relay may still connect directly
        if (!rateLimiter.TryAcquire(appId, Environment.TickCount64))
        {
            logger.LogDebug("No TURN login for {identity} app {appId}: over {limit} a minute",
                tenant, appId, TurnCredentialRateLimiter.PermitsPerMinute);
            return new IceServersResponse { IceServers = iceServers };
        }

        var login = TurnCredential.Create(odinContext.Tenant, turn.SharedSecret, UnixTimeUtc.Now(), turn.CredentialLifetimeSeconds);
        iceServers.Add(new IceServer
        {
            Urls = turn.ClientUrls(tenant),
            Username = login.Username,
            Credential = login.Credential
        });

        // Never the credential itself
        logger.LogInformation("TURN login for {identity} to call {peer} from app {appId}, expires {expiresAt}",
            tenant, peer, appId, login.ExpiresAt);

        return new IceServersResponse { IceServers = iceServers, ExpiresAt = login.ExpiresAt };
    }
}
