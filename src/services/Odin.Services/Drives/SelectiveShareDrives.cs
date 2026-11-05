#nullable enable
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
    /// Who writes and sees a drive's shares: the owner console, or the owner's app that owns the drive
    /// (docs/selective-share-pr3-plan.md, sections 3 and 5).
    /// </summary>
    public static bool CallerManagesShares(StorageDrive drive, CallerContext? caller) =>
        caller?.HasMasterKey == true ||
        (caller?.IsOwner == true && caller.OdinClientContext?.AppId?.Value is { } appId && appId == drive.AppId);

    /// <summary>
    /// Refuses unless <paramref name="drive"/> is Selective Share and <paramref name="caller"/> manages its shares
    /// (<see cref="CallerManagesShares"/>), before anything reads or writes them: <paramref name="doing"/> names what was asked.
    /// </summary>
    public static void AssertCallerManagesShares(StorageDrive drive, CallerContext? caller, string doing)
    {
        if (!drive.IsSelectiveShare)
        {
            throw new OdinClientException($"Drive {drive.Id} isn't a SelectiveShare drive: its files have no shares",
                OdinClientErrorCode.ArgumentError);
        }

        if (!CallerManagesShares(drive, caller))
        {
            throw new OdinSecurityException($"Only the owner console or the app that owns drive {drive.Id} {doing}");
        }
    }

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
    /// A file's ACL against its drive, on an upload or update by an owner caller: an owner-only drive's files are all
    /// <c>Owner</c>, and a Private Share drive's ACL is what <see cref="AssertAclFits"/> allows.
    /// </summary>
    public static void AssertAclFitsDrive(StorageDrive drive, AccessControlList acl)
    {
        if (drive.OwnerOnly && acl.RequiredSecurityGroup != SecurityGroupType.Owner)
        {
            throw new OdinClientException("Drive is owner only so all files must have RequiredSecurityGroup of Owner",
                OdinClientErrorCode.DriveSecurityAndAclMismatch);
        }

        AssertAclFits(drive, acl);
    }

    /// <summary>
    /// A file's ACL, on upload and update.  Named circles and identities are refused: the share rows say who.  Of the
    /// ladder only <c>Owner</c> and <c>Connected</c>: the drive is never public, and below <c>Connected</c> an
    /// unreviewed connection could read an unencrypted shared file.  The ladder stays, so
    /// the others can be allowed later (links).
    /// </summary>
    internal static void AssertAclFits(StorageDrive drive, AccessControlList acl)
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
