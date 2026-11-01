using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.Management;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;

namespace Odin.Services.Authorization.Acl
{
    public class DriveAclAuthorizationService(
        CircleNetworkService circleNetwork,
        CircleDefinitionService circleDefinitionService,
        TenantContext tenantContext,
        IDriveManager driveManager,
        TableDriveAclIndex driveAclIndex)
        : IDriveAclAuthorizationService
    {
        /// <summary>
        /// Whether <paramref name="odinId"/> could read a file with this ACL if they called in themselves:
        /// the same ACL decision as <see cref="CallerMayReadAsync"/>, from their connection record instead of a
        /// caller context.  Used where we send to someone rather than answer them (feed distribution).
        /// </summary>
        public async Task<bool> IdentityHasPermissionAsync(OdinId odinId, AccessControlList acl, IOdinContext odinContext)
        {
            if (acl == null)
            {
                return false;
            }

            // Anyone we send to is admitted at least at Authenticated, so an ACL asking no more than that,
            // and naming no circles, is decided without reading their connection record.
            var requiredCircles = acl.GetRequiredCircles().ToList();
            if (requiredCircles.Count == 0 && (int)acl.RequiredSecurityGroup <= (int)SecurityGroupType.Authenticated)
            {
                return Allows(acl, SecurityGroupType.Authenticated, [], odinId);
            }

            var icr = await circleNetwork.GetIcrAsync(odinId, odinContext, overrideHack: true, tryUpgradeEncryption: false);

            // Anyone not connected is admitted at Authenticated, as a peer calling in unconnected is.
            if (!icr.IsConnected())
            {
                return Allows(acl, SecurityGroupType.Authenticated, [], odinId);
            }

            // A connection at Connected, lowered to Authenticated until reviewed (ReviewedSecurityTier), holding
            // its enabled circles only, as a caller's circles are: a disabled circle grants nothing.  Only the
            // circles the ACL names can matter.
            var circles = new List<Guid>();
            foreach (var circleId in (icr.PeerKeyStore?.CircleGrants.Keys ?? Enumerable.Empty<Guid>()).Intersect(requiredCircles))
            {
                if (await circleDefinitionService.IsEnabledAsync(circleId))
                {
                    circles.Add(circleId);
                }
            }

            return Allows(acl, ReviewedSecurityTier.For(tenantContext.DataVersionNumber, icr), circles, odinId);
        }

        public async Task<bool> CallerMayReadAsync(ServerFileHeader header, IOdinContext odinContext,
            IReadOnlySet<Guid> sharedWithCaller = null)
        {
            if (!ZkDriveFiles.VisibleTo(header, odinContext))
            {
                return false;
            }

            var caller = odinContext.Caller;
            if (PassesEveryAcl(caller))
            {
                return true;
            }

            var acl = header?.ServerMetadata?.AccessControlList;
            if (acl == null || caller == null)
            {
                return false;
            }

            var level = ReviewedSecurityTier.EffectiveLevel(tenantContext, caller);
            var file = header.FileMetadata.File;
            if (await IsSelectiveShareAsync(file.DriveId))
            {
                // The share rows say who, not the ACL's names (docs/file-access.md, "With Selective Share")
                sharedWithCaller ??= await FilesSharedWithCallerAsync(file.DriveId, [file.FileId], odinContext);
                return sharedWithCaller.Contains(file.FileId) && ReachesGroup(acl, level);
            }

            return Allows(acl, level, caller.Circles?.Select(c => c.Value).ToList() ?? [], caller.OdinId);
        }

        public async Task<IReadOnlySet<Guid>> FilesSharedWithCallerAsync(Guid driveId, IReadOnlyCollection<Guid> fileIds,
            IOdinContext odinContext)
        {
            if (PassesEveryAcl(odinContext.Caller) || !await IsSelectiveShareAsync(driveId))
            {
                return null;
            }

            var rows = await driveAclIndex.GetLiveRowsAsync(driveId, fileIds, CallerMemberIds(odinContext), UnixTimeUtc.Now());
            return rows.Select(r => r.fileId).ToHashSet();
        }

        /// <summary>
        /// Who a file's index rows may name for this caller: their circles and their own OdinId's hash (none when
        /// anonymous).  The query (<c>DriveQuery</c>) and the read rule ask the same list.
        /// </summary>
        public static List<Guid> CallerMemberIds(IOdinContext odinContext)
        {
            var caller = odinContext.Caller;
            var members = new List<Guid>();
            if (!caller.IsAnonymous)
            {
                members.Add(odinContext.GetCallerOdinIdOrFail().ToHashId());
            }

            members.AddRange(caller.Circles?.Select(c => c.Value) ?? []);
            return members;
        }

        // Owner callers and the server's own work pass every file's ACL part
        public static bool PassesEveryAcl(CallerContext caller) =>
            (caller?.IsOwner ?? false) || caller?.SecurityLevel == SecurityGroupType.System;

        private async Task<bool> IsSelectiveShareAsync(Guid driveId) =>
            (await driveManager.GetDriveAsync(driveId))?.IsSelectiveShare == true;

        /// <summary>
        /// The one ACL decision, for <paramref name="reader"/> (null when anonymous) at <paramref name="level"/>
        /// holding <paramref name="circles"/>.
        /// </summary>
        /// <remarks>
        /// The security groups are a ladder -- Anonymous &lt; Authenticated &lt; AutoConnected &lt; Connected &lt;
        /// Owner -- and a reader meets every rung at or below their own, the same rule drive queries apply
        /// (<c>DriveQuery</c>, <c>IntRange(0, level)</c>).  Owner and System as callers never get here: they are
        /// shortcuts in front.  Named circles and named identities narrow the ACL further, together: the reader
        /// must be in one of the circles <em>or</em> be one of the identities, the same rule drive queries apply
        /// (<c>aclAnyOf</c>).  Neither replaces the security group.
        /// </remarks>
        public static bool Allows(AccessControlList acl, SecurityGroupType level, IReadOnlyCollection<Guid> circles, OdinId? reader)
        {
            if (!ReachesGroup(acl, level))
            {
                return false;
            }

            var requiredCircles = acl.GetRequiredCircles().ToList();
            var requiredIdentities = acl.GetRequiredIdentities().ToList();
            if (requiredCircles.Count == 0 && requiredIdentities.Count == 0)
            {
                return true;
            }

            return requiredCircles.Intersect(circles).Any() ||
                   (reader.HasValue && requiredIdentities.Any(id => OdinId.IsValid(id) && (OdinId)id == reader.Value));
        }

        /// <summary>Whether <paramref name="level"/> reaches the file's required group, on the ladder.</summary>
        private static bool ReachesGroup(AccessControlList acl, SecurityGroupType level) =>
            // System is a caller level, never a file's ACL; refuse it rather than let its value (1) admit everyone.
            acl.RequiredSecurityGroup != SecurityGroupType.System && (int)level >= (int)acl.RequiredSecurityGroup;
    }
}