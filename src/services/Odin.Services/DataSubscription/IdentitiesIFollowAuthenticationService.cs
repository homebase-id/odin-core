using System;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Storage.SQLite;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Mediator;

namespace Odin.Services.DataSubscription;

public class IdentitiesIFollowAuthenticationService
{
    private readonly OdinContextCache _cache;
    private readonly TenantContext _tenantContext;
    private readonly FollowerService _followerService;

    public IdentitiesIFollowAuthenticationService(OdinConfiguration config, FollowerService followerService, TenantContext tenantContext)
    {
        _followerService = followerService;
        _tenantContext = tenantContext;
        _cache = new OdinContextCache(config.Host.CacheSlidingExpirationSeconds);
    }

    /// <summary>
    /// Gets the <see cref="GetDotYouContext"/> for the specified token from cache or disk.
    /// </summary>
    public async Task<IOdinContext> GetDotYouContext(OdinId callerOdinId, ClientAuthenticationToken token, DatabaseConnection cn)
    {
        //Note: there's no CAT for alpha as we're supporting just feeds
        // for authentication, we manually check against the list of people I follow
        // therefore, just fabricate a token

        //TODO: i still dont think this is secure.  hmm let me think
        var callerGuidId = callerOdinId.ToHashId();
        var recipientGuidId = _tenantContext.HostOdinId.ToHashId();
        var tempToken = new ClientAuthenticationToken()
        {
            Id = callerGuidId,
            AccessTokenHalfKey = recipientGuidId.ToByteArray().ToSensitiveByteArray(),
            ClientTokenType = ClientTokenType.DataProvider
        };

        var creator = new Func<Task<IOdinContext>>(async delegate
        {
            var dotYouContext = new OdinContext();
            var (callerContext, permissionContext) = await GetPermissionContext(callerOdinId, tempToken, cn);

            if (null == permissionContext || callerContext == null)
            {
                return null;
            }

            dotYouContext.Caller = callerContext;
            dotYouContext.SetPermissionContext(permissionContext);

            return dotYouContext;
        });

        return await _cache.GetOrAddContext(tempToken, creator);
    }

    private async Task<(CallerContext callerContext, PermissionContext permissionContext)> GetPermissionContext(OdinId callerOdinId,
        ClientAuthenticationToken token, DatabaseConnection cn)
    {
        var permissionContext = await _followerService.CreatePermissionContextForIdentityIFollow(callerOdinId, token, cn);
        var cc = new CallerContext(
            odinId: callerOdinId,
            masterKey: null,
            securityLevel: SecurityGroupType.Authenticated,
            circleIds: null,
            tokenType: ClientTokenType.DataProvider);

        return (cc, permissionContext);
    }

}