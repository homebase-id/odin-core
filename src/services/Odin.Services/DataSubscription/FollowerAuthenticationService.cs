﻿using System;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.DataSubscription.Follower;

namespace Odin.Services.DataSubscription;

/// <summary>
/// Authenticates calls using the 
/// </summary>
public class FollowerAuthenticationService
{
    private readonly FollowerService _followerService;
    private readonly SharedOdinContextCache<FollowerAuthenticationService> _cache;

    public FollowerAuthenticationService(
        FollowerService followerService,
        SharedOdinContextCache<FollowerAuthenticationService> cache)
    {
        _followerService = followerService;
        _cache = cache;
    }

    /// <summary>
    /// Gets the <see cref="OdinContext"/> for the specified token from cache or disk.
    /// </summary>
    public async Task<IOdinContext> GetDotYouContextAsync(OdinId callerOdinId, ClientAuthenticationToken token)
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

        var creator = new Func<Task<IOdinContext>>(async delegate
        {
            var dotYouContext = new OdinContext();
            var (callerContext, permissionContext) = await GetPermissionContextAsync(callerOdinId, tempToken);

            if (null == permissionContext || callerContext == null)
            {
                return null;
            }

            dotYouContext.Caller = callerContext;
            dotYouContext.SetPermissionContext(permissionContext);

            return dotYouContext;
        });

        return await _cache.GetOrAddContextAsync(tempToken, creator);
    }

    private async Task<(CallerContext callerContext, PermissionContext permissionContext)> GetPermissionContextAsync(OdinId callerOdinId, ClientAuthenticationToken token)
    {
        var permissionContext = await _followerService.CreateFollowerPermissionContextAsync(callerOdinId, token);
        var cc = new CallerContext(
            odinId: callerOdinId,
            masterKey: null,
            securityLevel: SecurityGroupType.Authenticated,
            circleIds: null,
            tokenType: ClientTokenType.DataProvider);

        return (cc, permissionContext);
    }
}