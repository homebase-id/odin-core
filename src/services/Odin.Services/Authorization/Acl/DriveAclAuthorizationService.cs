using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage;
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

        public async Task<bool> CallerMayReadAsync(ServerFileHeader header, IOdinContext odinContext, CallerShares sharedWithCaller = null)
        {
            var visible = ZkDriveFiles.VisibleTo(header, odinContext);
            var caller = odinContext.Caller;
            if (PassesEveryAcl(caller))
            {
                return visible;
            }

            var acl = header?.ServerMetadata?.AccessControlList;
            if (acl == null || caller == null)
            {
                return false;
            }

            var level = ReviewedSecurityTier.EffectiveLevel(tenantContext, caller);
            var file = header.FileMetadata.File;
            // Shares given: the caller's on a Selective Share drive, already looked up
            if (sharedWithCaller != null || await IsSelectiveShareAsync(file.DriveId))
            {
                // The share rows say who, not the ACL's names (docs/file-access.md, "With Selective Share"); a live
                // share also opens a client-locked file to the member, who gets the share's locked file key (section 6)
                sharedWithCaller ??= await SharesOfAsync(file.DriveId, [header], odinContext);
                return sharedWithCaller.LiveRowOf(file.FileId) != null && ReachesGroup(acl, level);
            }

            return visible && Allows(acl, level, caller.Circles?.Select(c => c.Value).ToList() ?? [], caller.OdinId);
        }

        public async Task<CallerShares> FilesSharedWithCallerAsync(Guid driveId, IReadOnlyCollection<ServerFileHeader> headers,
            IOdinContext odinContext)
        {
            if (PassesEveryAcl(odinContext.Caller) || !await IsSelectiveShareAsync(driveId))
            {
                return null;
            }

            return await SharesOfAsync(driveId, headers, odinContext);
        }

        public async Task<bool> CallerHoldsShareAsync(ServerFileHeader header, DrivePermission permission, IOdinContext odinContext)
        {
            if (PassesEveryAcl(odinContext.Caller))
            {
                return true;
            }

            var acl = header?.ServerMetadata?.AccessControlList;
            if (acl == null || odinContext.Caller == null)
            {
                return false;
            }

            var file = header.FileMetadata.File;
            var shares = await SharesOfAsync(file.DriveId, [header], odinContext);
            return shares.Grants(file.FileId, permission) && ReachesGroup(acl, ReviewedSecurityTier.EffectiveLevel(tenantContext, odinContext.Caller));
        }

        /// <summary>
        /// The rows naming the caller, live or not, that decide each of <paramref name="headers"/>: a file's own, and a
        /// comment's those of the file it comments on, which its group names (docs/selective-share-pr3-plan.md,
        /// section 10).  One read.
        /// </summary>
        private async Task<CallerShares> SharesOfAsync(Guid driveId, IReadOnlyCollection<ServerFileHeader> headers, IOdinContext odinContext)
        {
            var commentedFiles = headers.Select(h => (h.FileMetadata.File.FileId, CommentedFile: CommentedFileOf(h))).ToList();
            var rows = await driveAclIndex.GetRowsAsync(driveId,
                commentedFiles.Where(f => f.CommentedFile == null).Select(f => f.FileId).ToList(),
                commentedFiles.Where(f => f.CommentedFile != null).Select(f => f.CommentedFile.Value).Distinct().ToList(),
                CallerMemberIds(odinContext));

            // A comment is read exactly when its file is: held back, too, when the caller's level doesn't reach the file's
            // group (a file set back to Owner), whatever the comment's own
            var level = ReviewedSecurityTier.EffectiveLevel(tenantContext, odinContext.Caller);
            var byFileId = rows.ToLookup(r => r.Row.fileId);
            var byGlobalTransitId = rows.ToLookup(r => r.GlobalTransitId);
            var byFile = commentedFiles.ToDictionary(f => f.FileId,
                f => (f.CommentedFile == null ? byFileId[f.FileId] : byGlobalTransitId[f.CommentedFile]).ToList());
            return new CallerShares(
                byFile.ToDictionary(f => f.Key, f => f.Value.Select(r => r.Row).ToList()),
                UnixTimeUtc.Now(),
                byFile.Where(f => f.Value.Any(r => !ReachesGroup((SecurityGroupType)r.RequiredSecurityGroup, level))).Select(f => f.Key).ToHashSet());
        }

        /// <summary>
        /// A comment's file, by the global transit id its group holds (CommentStreamWriter); null for any other file.  A
        /// comment without a group comments on nothing a row can reach.
        /// </summary>
        private static Guid? CommentedFileOf(ServerFileHeader header) =>
            header.ServerMetadata?.FileSystemType == FileSystemType.Comment
                ? header.FileMetadata.AppData?.GroupId ?? Guid.Empty
                : null;

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
            (caller?.IsOwner ?? false) || caller?.IsSystem == true;

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
        private static bool ReachesGroup(AccessControlList acl, SecurityGroupType level) => ReachesGroup(acl.RequiredSecurityGroup, level);

        private static bool ReachesGroup(SecurityGroupType required, SecurityGroupType level) =>
            // System is a caller level, never a file's ACL; refuse it rather than let its value (1) admit everyone.
            required != SecurityGroupType.System && (int)level >= (int)required;
    }
}