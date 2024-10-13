using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Odin.Core.Identity;
using Odin.Services.AppNotifications.SystemNotifications;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Membership.Connections;
namespace Odin.Services.Authentication.Transit;

public class TransitAuthenticationService :
    INotificationHandler<ConnectionFinalizedNotification>,
    INotificationHandler<ConnectionBlockedNotification>,
    INotificationHandler<ConnectionDeletedNotification>
{
    private readonly OdinContextCache _cache;
    private readonly CircleNetworkService _circleNetworkService;

    public TransitAuthenticationService(CircleNetworkService circleNetworkService, OdinConfiguration config)
    {
        _circleNetworkService = circleNetworkService;
        _cache = new OdinContextCache(config.Host.CacheSlidingExpirationSeconds);
    }

    /// <summary>
    /// Gets the <see cref="GetDotYouContext"/> for the specified token from cache or disk.
    /// </summary>
    public async Task<IOdinContext> GetDotYouContext(OdinId callerOdinId, ClientAuthenticationToken token, IOdinContext odinContext, IdentityDatabase db)
    {
        var creator = new Func<Task<IOdinContext>>(async delegate
        {
            var dotYouContext = new OdinContext();
            var (callerContext, permissionContext) = await GetPermissionContext(callerOdinId, token, odinContext, db);

            if (null == permissionContext || callerContext == null)
            {
                return null;
            }

            dotYouContext.Caller = callerContext;
            dotYouContext.SetPermissionContext(permissionContext);

            return dotYouContext;
        });

        return await _cache.GetOrAddContext(token, creator);
    }

    private async Task<(CallerContext callerContext, PermissionContext permissionContext)> GetPermissionContext(OdinId callerOdinId,
        ClientAuthenticationToken token, IOdinContext odinContext, IdentityDatabase db)
    {
        var (permissionContext, circleIds) = await _circleNetworkService.CreateTransitPermissionContext(callerOdinId, token, odinContext, db);
        var cc = new CallerContext(
            odinId: callerOdinId,
            masterKey: null,
            securityLevel: SecurityGroupType.Connected,
            circleIds: circleIds);

        return (cc, permissionContext);
    }

    public Task Handle(ConnectionFinalizedNotification notification, CancellationToken cancellationToken)
    {
        // _cache.EnqueueIdentityForReset(notification.OdinId);
        _cache.Reset();
        return Task.CompletedTask;
    }

    public Task Handle(ConnectionBlockedNotification notification, CancellationToken cancellationToken)
    {
        // _cache.EnqueueIdentityForReset(notification.OdinId);
        _cache.Reset();
        return Task.CompletedTask;
    }

    public Task Handle(ConnectionDeletedNotification notification, CancellationToken cancellationToken)
    {
        // _cache.EnqueueIdentityForReset(notification.OdinId);
        _cache.Reset();
        return Task.CompletedTask;
    }
}