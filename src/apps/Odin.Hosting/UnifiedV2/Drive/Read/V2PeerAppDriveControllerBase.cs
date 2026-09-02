using System;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Peer.Outgoing.Drive.Query;

namespace Odin.Hosting.UnifiedV2.Drive.Read;

/// <summary>
/// Shared plumbing for the slug-addressed peer routes,
/// <c>/peer/{odinId}/apps/{appSlug}/drives/{driveSlug}/…</c> (docs/drive-addressing.md).
/// </summary>
/// <remarks>
/// These routes exist so a caller can address another identity's drive without both hosts sharing
/// hardcoded guid constants.  The slug is resolved by the recipient — only the host holding the drive
/// knows what <c>chat</c> means there — and the resolved <see cref="TargetDrive"/> is then handed to
/// the same <see cref="PeerDriveQueryService"/> methods the guid routes use.  Nothing about the
/// operation changes; only how the drive was named.
///
/// The guid routes synthesize <c>TargetDrive { Alias = driveId, Type = Guid.Empty }</c> and let the
/// remote match on alias alone.  Resolution returns the real pair, so these routes carry a complete
/// <see cref="TargetDrive"/> — strictly more specific than what the guid form sends.
/// </remarks>
public abstract class V2PeerAppDriveControllerBase(PeerDriveQueryService peerDriveQueryService)
    : OdinControllerBase
{
    /// <summary>
    /// Validates the recipient and asks it what <c>/apps/{appSlug}/drives/{driveSlug}</c> names there.
    /// Throws when nothing answers to that address, which includes a drive this identity may not read
    /// — the remote does not distinguish the two, so neither can this.
    /// </summary>
    protected async Task<(OdinId Recipient, TargetDrive TargetDrive)> ResolveAsync(
        string odinId, string appSlug, string driveSlug)
    {
        AssertIsValidOdinId(odinId, out var id);
        var targetDrive = await peerDriveQueryService.ResolveRemoteDriveAsync(id, appSlug, driveSlug, WebOdinContext);
        return (id, targetDrive);
    }

    protected static ExternalFileIdentifier ToExternalFile(TargetDrive targetDrive, Guid fileId)
    {
        return new ExternalFileIdentifier
        {
            FileId = fileId,
            TargetDrive = targetDrive
        };
    }

    protected static GlobalTransitIdFileIdentifier ToGtidFile(TargetDrive targetDrive, Guid gtid)
    {
        return new GlobalTransitIdFileIdentifier
        {
            GlobalTransitId = gtid,
            TargetDrive = targetDrive
        };
    }

    protected static FileQueryParamsV1 ToV1QueryParams(FileQueryParams p, TargetDrive targetDrive)
    {
        return new FileQueryParamsV1
        {
            TargetDrive = targetDrive,
            FileType = p.FileType,
            FileState = p.FileState,
            DataType = p.DataType,
            ArchivalStatus = p.ArchivalStatus,
            Sender = p.Sender,
            GroupId = p.GroupId,
            UserDate = p.UserDate,
            ClientUniqueIdAtLeastOne = p.ClientUniqueIdAtLeastOne,
            TagsMatchAtLeastOne = p.TagsMatchAtLeastOne,
            TagsMatchAll = p.TagsMatchAll,
            LocalTagsMatchAtLeastOne = p.LocalTagsMatchAtLeastOne,
            LocalTagsMatchAll = p.LocalTagsMatchAll,
            GlobalTransitId = p.GlobalTransitId
        };
    }
}
