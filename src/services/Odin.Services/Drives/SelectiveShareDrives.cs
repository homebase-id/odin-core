#nullable enable
using System.Collections.Generic;
using Odin.Core.Exceptions;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;

namespace Odin.Services.Drives;

/// <summary>
/// What a Selective Share drive refuses (docs/selective-share-pr3-plan.md, section 1).  Its share rows say who may see
/// a file, so a file's ACL names no one, and a file is either the owner callers' (<c>Owner</c>) or shared
/// (<c>Connected</c>, read through a live share row).
/// </summary>
public static class SelectiveShareDrives
{
    /// <summary>
    /// Whether a file's share rows, not its ACL, decide what <paramref name="caller"/> may do on <paramref name="drive"/>:
    /// a Selective Share drive and anyone but owner callers and the System (docs/file-access.md, "With Selective Share").
    /// </summary>
    public static bool RowsDecideFor(StorageDrive drive, CallerContext? caller) =>
        drive.IsSelectiveShare && !DriveAclAuthorizationService.PassesEveryAcl(caller);

    /// <summary>
    /// A shared file is Connected, so its members reach its group: a share lifts an Owner file (decision A of
    /// docs/selective-share-pr3-plan.md).  True when it lifted.
    /// </summary>
    public static bool LiftForShares(AccessControlList acl)
    {
        if (acl.RequiredSecurityGroup != SecurityGroupType.Owner)
        {
            return false;
        }

        acl.RequiredSecurityGroup = SecurityGroupType.Connected;
        return true;
    }

    /// <summary>
    /// A drive's settings, when it is created, converted or its attributes change.  <c>OwnerOnly</c> keeps every file
    /// <c>Owner</c>, so such a drive could share nothing; the collaborative channel's edit rule is what a share row's
    /// <c>Write</c> bit says, and its other effects move copies, which a zero-knowledge drive never sends
    /// (docs/file-access.md, "Collaborative channels").
    /// </summary>
    public static void AssertSettingsFit(DriveSharingModel sharingModel, bool ownerOnly, IReadOnlyDictionary<string, string>? attributes)
    {
        if (sharingModel != DriveSharingModel.SelectiveShare)
        {
            return;
        }

        if (ownerOnly)
        {
            throw new OdinClientException("A SelectiveShare drive can't be owner only: none of its files could be shared",
                OdinClientErrorCode.ArgumentError);
        }

        if (BuiltInDriveAttributes.IsCollaborativeChannelIn(attributes))
        {
            throw new OdinClientException($"A SelectiveShare drive can't be a collaborative channel " +
                                          $"({BuiltInDriveAttributes.IsCollaborativeChannel}): its share rows say who may write",
                OdinClientErrorCode.ArgumentError);
        }
    }

    /// <summary>
    /// A file's ACL, on upload and update.  Named circles and identities are refused: the share rows say who.  Of the
    /// ladder only <c>Owner</c> and <c>Connected</c>: the drive is never public, and below <c>Connected</c> an
    /// unreviewed connection could read an unencrypted shared file (decided 2026-10-31).  The ladder stays, so
    /// the others can be allowed later (links).
    /// </summary>
    public static void AssertAclFits(StorageDrive drive, AccessControlList acl)
    {
        if (!drive.IsSelectiveShare)
        {
            return;
        }

        if (acl.CircleIdList?.Count > 0 || acl.OdinIdList?.Count > 0)
        {
            throw new OdinClientException("A file on a SelectiveShare drive names no circles or identities: its shares say who",
                OdinClientErrorCode.DriveSecurityAndAclMismatch);
        }

        if (acl.RequiredSecurityGroup is not (SecurityGroupType.Owner or SecurityGroupType.Connected))
        {
            throw new OdinClientException($"A file on a SelectiveShare drive is Owner or Connected, not {acl.RequiredSecurityGroup}",
                OdinClientErrorCode.DriveSecurityAndAclMismatch);
        }
    }
}
