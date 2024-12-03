using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Services.Base;
using Odin.Services.Drives.Management;
using Odin.Services.Membership.Connections;

namespace Odin.Services.Authorization.Acl
{
    public class DriveAclAuthorizationService(
        CircleNetworkService circleNetwork,
        DriveManager driveManager,
        ILogger<DriveAclAuthorizationService> logger)
        : IDriveAclAuthorizationService
    {
        public async Task AssertCallerMatchesAclAsync(Guid driveId, AccessControlList acl, IOdinContext odinContext)
        {
            ThrowWhenFalse(await CallerMatchesAclAsync(driveId, acl, odinContext));
        }

        public async Task<bool> IdentityMatchesAclAsync(Guid driveId, OdinId odinId, AccessControlList acl, IOdinContext odinContext)
        {
            var appliedAcl = acl;

            if (appliedAcl == null)
            {
                appliedAcl = (await driveManager.GetDriveAsync(driveId)).DefaultReadAcl;

                //there must be an acl
                if (appliedAcl == null)
                {
                    return false;
                }
            }

            //if file has required circles, see if caller has at least one
            var requiredCircles = appliedAcl.GetRequiredCircles().ToList();
            if (requiredCircles.Any())
            {
                var icr = await circleNetwork.GetIcrAsync(odinId, odinContext, true);
                var hasBadData = icr.AccessGrant.CircleGrants?.Where(cg => cg.Value?.CircleId?.Value == null).Any();
                if (hasBadData.GetValueOrDefault())
                {
                    var cg = icr.AccessGrant.CircleGrants?.Select(cg => cg.Value.Redacted());
                    logger.LogInformation("ICR for {odinId} has corrupt circle grants. {cg}", odinId, cg);

                    //let it continue on
                }

                var hasAtLeastOneCircle = requiredCircles
                    .Intersect(icr.AccessGrant.CircleGrants?.Select(cg => cg.Value.CircleId.Value) ?? Array.Empty<Guid>())
                    .Any();
                return hasAtLeastOneCircle;
            }

            if (appliedAcl.GetRequiredIdentities().Any())
            {
                return false;
            }

            switch (appliedAcl.RequiredSecurityGroup)
            {
                case SecurityGroupType.Anonymous:
                    return true;

                case SecurityGroupType.Connected:
                    return (await circleNetwork.GetIcrAsync(odinId, odinContext, true)).IsConnected();
            }

            return false;
        }

        public async Task<bool> CallerMatchesAclAsync(Guid driveId, AccessControlList acl, IOdinContext odinContext)
        {
            var caller = odinContext.Caller;
            if (caller?.IsOwner ?? false)
            {
                return true;
            }

            if (caller?.SecurityLevel == SecurityGroupType.System)
            {
                return true;
            }

            var appliedAcl = acl;
            if (appliedAcl == null)
            {
                appliedAcl = (await driveManager.GetDriveAsync(driveId)).DefaultReadAcl;

                //there must be an acl
                if (appliedAcl == null)
                {
                    return false;
                }
            }

            //if file has required circles, see if caller has at least one
            var requiredCircles = appliedAcl.GetRequiredCircles().ToList();
            if (requiredCircles.Any() && !requiredCircles.Intersect(caller!.Circles.Select(c => c.Value)).Any())
            {
                return false;
            }

            if (appliedAcl.GetRequiredIdentities().Any())
            {
                throw new NotImplementedException("TODO: enforce logic for required identities");
            }

            switch (appliedAcl.RequiredSecurityGroup)
            {
                case SecurityGroupType.Anonymous:
                    return true;

                case SecurityGroupType.Authenticated:
                    return ((int)caller!.SecurityLevel) >= (int)SecurityGroupType.Authenticated;

                case SecurityGroupType.AutoConnected:
                case SecurityGroupType.Connected:
                    return await CallerIsConnected(odinContext);
            }

            return false;
        }

        private void ThrowWhenFalse(bool eval)
        {
            if (eval == false)
            {
                throw new OdinSecurityException("I'm throwing because it's false!");
            }
        }

        private Task<bool> CallerIsConnected(IOdinContext odinContext)
        {
            //TODO: cache result - 
            return Task.FromResult(odinContext.Caller.IsConnected);
        }
    }
}