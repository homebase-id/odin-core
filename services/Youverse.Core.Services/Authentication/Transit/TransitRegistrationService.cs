using System;
using System.Threading.Tasks;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Configuration;
using Youverse.Core.Services.Contacts.Circle.Membership;

namespace Youverse.Core.Services.Authentication.Transit;

//TODO: the name 'registration' is not accurate here because nothing is being registered.  this is just a cache loader
//
public class TransitRegistrationService
{
    private readonly DotYouContextCache _cache;
    private readonly ICircleNetworkService _circleNetworkService;
    
    public TransitRegistrationService(ICircleNetworkService circleNetworkService, YouverseConfiguration config)
    {
        _circleNetworkService = circleNetworkService;
        _cache = new DotYouContextCache(config.Host.CacheSlidingExpirationSeconds);

    }
    
    /// <summary>
    /// Gets the <see cref="GetDotYouContext"/> for the specified token from cache or disk.
    /// </summary>
    public async Task<DotYouContext> GetDotYouContext(DotYouIdentity callerDotYouId, ClientAuthenticationToken token)
    {
        var creator = new Func<Task<DotYouContext>>(async delegate
        {
            var dotYouContext = new DotYouContext();
            var (callerContext, permissionContext) = await GetPermissionContext(callerDotYouId, token);

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

    private async Task<(CallerContext callerContext, PermissionContext permissionContext)> GetPermissionContext(DotYouIdentity callerDotYouId, ClientAuthenticationToken token)
    {
        var (permissionContext, circleIds) = await _circleNetworkService.CreateTransitPermissionContext(callerDotYouId, token);
        var cc =  new CallerContext(
            dotYouId: callerDotYouId,
            masterKey: null,
            securityLevel: SecurityGroupType.Connected,
            circleIds:circleIds);
        
        return (cc, permissionContext);
    }
}