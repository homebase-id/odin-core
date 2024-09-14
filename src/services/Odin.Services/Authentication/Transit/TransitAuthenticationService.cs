using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Odin.Core.Identity;
using Odin.Core.Storage.SQLite;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Mediator;
using Odin.Services.Membership.Connections;

namespace Odin.Services.Authentication.Transit;

public class TransitAuthenticationService : INotificationHandler<IdentityConnectionRegistrationChangedNotification>
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
    public async Task<IOdinContext> GetDotYouContext(OdinId callerOdinId, ClientAuthenticationToken token, IOdinContext odinContext, DatabaseConnection cn)
    {
        var creator = new Func<Task<IOdinContext>>(async delegate
        {
            var dotYouContext = new OdinContext();
            var (callerContext, permissionContext) = await GetPermissionContext(callerOdinId, token, odinContext, cn);

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
        ClientAuthenticationToken token, IOdinContext odinContext, DatabaseConnection cn)
    {
        var (permissionContext, circleIds) = await _circleNetworkService.CreateTransitPermissionContext(callerOdinId, token, odinContext, cn);
        var cc = new CallerContext(
            odinId: callerOdinId,
            masterKey: null,
            securityLevel: SecurityGroupType.ConfirmConnected,
            circleIds: circleIds);

        return (cc, permissionContext);
    }

    public Task Handle(IdentityConnectionRegistrationChangedNotification notification, CancellationToken cancellationToken)
    {
        // _cache.EnqueueIdentityForReset(notification.OdinId);
        _cache.Reset();
        return Task.CompletedTask;
    }
}