﻿using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Odin.Core.Identity;
using Odin.Services.AppNotifications.SystemNotifications;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Membership.Connections;
namespace Odin.Services.Authentication.Transit;

public class TransitAuthenticationService :
    INotificationHandler<ConnectionFinalizedNotification>,
    INotificationHandler<ConnectionBlockedNotification>,
    INotificationHandler<ConnectionDeletedNotification>
{
    private readonly OdinContextCache _cache;
    private readonly CircleNetworkService _circleNetworkService;

    public TransitAuthenticationService(OdinContextCache cache, CircleNetworkService circleNetworkService)
    {
        _cache = cache;
        _circleNetworkService = circleNetworkService;
    }

    /// <summary>
    /// Gets the <see cref="IOdinContext"/> for the specified token from cache or disk.
    /// </summary>
    public async Task<IOdinContext> GetDotYouContextAsync(OdinId callerOdinId, ClientAuthenticationToken token, IOdinContext odinContext)
    {
        var creator = new Func<Task<IOdinContext>>(async () =>
        {
            var dotYouContext = new OdinContext();
            var (callerContext, permissionContext) = await GetPermissionContextAsync(callerOdinId, token, odinContext);

            if (null == permissionContext || callerContext == null)
            {
                return null;
            }

            dotYouContext.Caller = callerContext;
            dotYouContext.SetPermissionContext(permissionContext);

            return dotYouContext;
        });

        return await _cache.GetOrAddContextAsync(token, creator);
    }

    private async Task<(CallerContext callerContext, PermissionContext permissionContext)> GetPermissionContextAsync(OdinId callerOdinId,
        ClientAuthenticationToken token, IOdinContext odinContext)
    {
        var (permissionContext, circleIds) = await _circleNetworkService.CreateTransitPermissionContextAsync(callerOdinId, token, odinContext);
        var cc = new CallerContext(
            odinId: callerOdinId,
            masterKey: null,
            securityLevel: SecurityGroupType.Connected,
            circleIds: circleIds);

        return (cc, permissionContext);
    }

    public async Task Handle(ConnectionFinalizedNotification notification, CancellationToken cancellationToken)
    {
        // _cache.EnqueueIdentityForReset(notification.OdinId);
        await _cache.ResetAsync();
    }

    public async Task Handle(ConnectionBlockedNotification notification, CancellationToken cancellationToken)
    {
        // _cache.EnqueueIdentityForReset(notification.OdinId);
        await _cache.ResetAsync();
    }

    public async Task Handle(ConnectionDeletedNotification notification, CancellationToken cancellationToken)
    {
        // _cache.EnqueueIdentityForReset(notification.OdinId);
        await _cache.ResetAsync();
    }
}