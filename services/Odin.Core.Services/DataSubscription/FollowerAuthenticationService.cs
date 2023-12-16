using System;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Services.AppNotifications.ClientNotifications;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Base;
using Odin.Core.Services.Configuration;
using Odin.Core.Services.DataSubscription.Follower;
using Odin.Core.Services.Mediator;

namespace Odin.Core.Services.DataSubscription;

/// <summary>
/// Authenticates calls using the 
/// </summary>
public class FollowerAuthenticationService
{
    private readonly OdinContextCache _cache;
    private readonly FollowerService _followerService;

    public FollowerAuthenticationService(OdinConfiguration config, FollowerService followerService)
    {
        _followerService = followerService;
        _cache = new OdinContextCache(config.Host.CacheSlidingExpirationSeconds);
    }

    /// <summary>
    /// Gets the <see cref="GetDotYouContext"/> for the specified token from cache or disk.
    /// </summary>
    public async Task<OdinContext> GetDotYouContext(OdinId callerOdinId, ClientAuthenticationToken token)
    {
        //Note: there's no CAT for alpha as we're supporting just feeds
        // for authentication, we manually check against the list of people I follow
        // therefore, just fabricate a token

        //TODO: i still dont think this is secure.  hmm let me think
        var guidId = callerOdinId.ToHashId();
        var tempToken = new ClientAuthenticationToken()
        {
            Id = guidId,
            AccessTokenHalfKey = guidId.ToByteArray().ToSensitiveByteArray(),
            ClientTokenType = ClientTokenType.DataProvider
        };

        var creator = new Func<Task<OdinContext>>(async delegate
        {
            var dotYouContext = new OdinContext();
            var (callerContext, permissionContext) = await GetPermissionContext(callerOdinId, tempToken);

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

    private async Task<(CallerContext callerContext, PermissionContext permissionContext)> GetPermissionContext(OdinId callerOdinId, ClientAuthenticationToken token)
    {
        var permissionContext = await _followerService.CreateFollowerPermissionContext(callerOdinId, token);
        var cc = new CallerContext(
            odinId: callerOdinId,
            masterKey: null,
            securityLevel: SecurityGroupType.Authenticated,
            circleIds: null,
            tokenType: ClientTokenType.DataProvider);

        return (cc, permissionContext);
    }

    public Task Handle(IdentityConnectionRegistrationChangedNotification notification, CancellationToken cancellationToken)
    {
        _cache.EnqueueIdentityForReset(notification.OdinId);
        return Task.CompletedTask;
    }
}