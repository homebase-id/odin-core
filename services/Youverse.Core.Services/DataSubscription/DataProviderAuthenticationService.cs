﻿using System;
using System.Threading;
using System.Threading.Tasks;
using Youverse.Core.Identity;
using Youverse.Core.Services.AppNotifications.ClientNotifications;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Configuration;
using Youverse.Core.Services.DataSubscription.Follower;

namespace Youverse.Core.Services.DataSubscription;

public class DataProviderAuthenticationService
{
    private readonly DotYouContextCache _cache;
    private readonly FollowerService _followerService;

    public DataProviderAuthenticationService( YouverseConfiguration config, FollowerService followerService)
    {
        _followerService = followerService;
        _cache = new DotYouContextCache(config.Host.CacheSlidingExpirationSeconds);
    }

    /// <summary>
    /// Gets the <see cref="GetDotYouContext"/> for the specified token from cache or disk.
    /// </summary>
    public async Task<DotYouContext> GetDotYouContext(OdinId callerDotYouId, ClientAuthenticationToken token)
    {
        //Note: there's no CAT for alpha as we're supporting just feeds
        // for authentication, we manually check against the list of people I follow
        // therefore, just fabricate a token
        
        //TODO: i still dont think this is secure.  hmm let me think
        var guidId = callerDotYouId.ToHashId();
        var tempToken = new ClientAuthenticationToken()
        {
            Id = guidId,
            AccessTokenHalfKey = guidId.ToByteArray().ToSensitiveByteArray(),
            ClientTokenType = ClientTokenType.DataProvider
        };

        var creator = new Func<Task<DotYouContext>>(async delegate
        {
            var dotYouContext = new DotYouContext();
            var (callerContext, permissionContext) = await GetPermissionContext(callerDotYouId, tempToken);

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

    private async Task<(CallerContext callerContext, PermissionContext permissionContext)> GetPermissionContext(OdinId callerDotYouId, ClientAuthenticationToken token)
    {
        var permissionContext = await _followerService.CreatePermissionContext(callerDotYouId, token);
        var cc = new CallerContext(
            dotYouId: callerDotYouId,
            masterKey: null,
            securityLevel: SecurityGroupType.Authenticated,
            circleIds: null,
            tokenType: ClientTokenType.DataProvider);

        return (cc, permissionContext);
    }

    public Task Handle(IdentityConnectionRegistrationChangedNotification notification, CancellationToken cancellationToken)
    {
        _cache.EnqueueIdentityForReset(notification.DotYouId);
        return Task.CompletedTask;
    }
}