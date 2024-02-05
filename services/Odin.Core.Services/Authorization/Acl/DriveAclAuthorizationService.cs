using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Services.Base;
using Odin.Core.Services.Membership.Connections;
using Serilog;

namespace Odin.Core.Services.Authorization.Acl
{
    public class DriveAclAuthorizationService : IDriveAclAuthorizationService
    {
        private readonly OdinContextAccessor _contextAccessor;
        private readonly CircleNetworkService _circleNetwork;
        private readonly ILogger<DriveAclAuthorizationService> _logger;


        public DriveAclAuthorizationService(OdinContextAccessor contextAccessor, CircleNetworkService circleNetwork,
            ILogger<DriveAclAuthorizationService> logger)
        {
            _contextAccessor = contextAccessor;
            _circleNetwork = circleNetwork;
            _logger = logger;
        }

        public Task AssertCallerHasPermission(AccessControlList acl)
        {
            ThrowWhenFalse(CallerHasPermission(acl).GetAwaiter().GetResult());

            return Task.CompletedTask;
        }

        public async Task<bool> IdentityHasPermission(OdinId odinId, AccessControlList acl)
        {
            //there must be an acl
            if (acl == null)
            {
                return false;
            }

            //if file has required circles, see if caller has at least one
            var requiredCircles = acl.GetRequiredCircles().ToList();
            if (requiredCircles.Any())
            {
                var icr = await _circleNetwork.GetIdentityConnectionRegistration(odinId, true);
                var hasBadData = icr.AccessGrant.CircleGrants?.Where(cg => cg.Value?.CircleId?.Value == null).Any();
                if (hasBadData.GetValueOrDefault())
                {
                    var cg = icr.AccessGrant.CircleGrants?.Select(cg => cg.Value.Redacted());
                    _logger.LogWarning("ICR for {odinId} has corrupt circle grants. {cg}", odinId, cg);
                    
                    //let it continue on
                }

                var hasAtLeastOneCircle = requiredCircles.Intersect(icr.AccessGrant.CircleGrants?.Select(cg => cg.Value.CircleId.Value) ?? Array.Empty<Guid>())
                    .Any();
                return hasAtLeastOneCircle;
            }

            if (acl.GetRequiredIdentities().Any())
            {
                return false;
            }

            switch (acl.RequiredSecurityGroup)
            {
                case SecurityGroupType.Anonymous:
                    return true;

                case SecurityGroupType.Connected:
                    return (await _circleNetwork.GetIdentityConnectionRegistration(odinId, true)).IsConnected();
            }

            return false;
        }

        public Task<bool> CallerHasPermission(AccessControlList acl)
        {
            var caller = _contextAccessor.GetCurrent().Caller;
            if (caller?.IsOwner ?? false)
            {
                return Task.FromResult(true);
            }

            if (caller?.SecurityLevel == SecurityGroupType.System)
            {
                return Task.FromResult(true);
            }

            //there must be an acl
            if (acl == null)
            {
                return Task.FromResult(false);
            }

            //if file has required circles, see if caller has at least one
            var requiredCircles = acl.GetRequiredCircles().ToList();
            if (requiredCircles.Any() && !requiredCircles.Intersect(caller!.Circles.Select(c => c.Value)).Any())
            {
                return Task.FromResult(false);
            }

            if (acl.GetRequiredIdentities().Any())
            {
                throw new NotImplementedException("TODO: enforce logic for required identities");
            }

            switch (acl.RequiredSecurityGroup)
            {
                case SecurityGroupType.Anonymous:
                    return Task.FromResult(true);

                case SecurityGroupType.Authenticated:
                    return Task.FromResult(((int)caller!.SecurityLevel) >= (int)SecurityGroupType.Authenticated);

                case SecurityGroupType.Connected:
                    return CallerIsConnected();
            }

            return Task.FromResult(false);
        }

        public async Task<bool> CallerIsConnected()
        {
            //TODO: cache result - 
            return await Task.FromResult(_contextAccessor.GetCurrent().Caller.IsConnected);
        }


        public Task<bool> CallerIsInList(List<string> odinIdList)
        {
            var inList = odinIdList.Any(s =>
                s.Equals(_contextAccessor.GetCurrent().GetCallerOdinIdOrFail().DomainName, StringComparison.InvariantCultureIgnoreCase));
            return Task.FromResult(inList);
        }

        private void ThrowWhenFalse(bool eval)
        {
            if (eval == false)
            {
                throw new OdinSecurityException("I'm throwing because it's false!");
            }
        }
    }
}