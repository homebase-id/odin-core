using System;
using System.Collections.Generic;
using System.Linq;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Services.Drives;

namespace Odin.Services.Authorization.Acl;

/// <summary>
/// On a Selective Share drive, the share rows naming an outside caller, live or not, for some files: a file's own, a
/// comment's those of the file it comments on (docs/selective-share-pr3-plan.md, section 10).
/// </summary>
public sealed class CallerShares(IReadOnlyDictionary<Guid, List<DriveAclIndexRecord>> rowsByFile, UnixTimeUtc now)
{
    /// <summary>
    /// The live row the caller reads <paramref name="fileId"/> through (docs/file-access.md, "live row"): with live rows in
    /// more than one of their circles, the lowest circle id's, so every read of the file picks the same one (section 6).
    /// Null when none.
    /// </summary>
    public DriveAclIndexRecord LiveRowOf(Guid fileId) => Live(fileId).MinBy(r => r.aclMemberId);

    /// <summary>Whether a live row grants the caller <paramref name="permission"/> on <paramref name="fileId"/>.</summary>
    public bool Grants(Guid fileId, DrivePermission permission) =>
        Live(fileId).Any(r => ((DrivePermission)r.permissions).HasFlag(permission));

    /// <summary>
    /// Whether a row names the caller, live or not: a member a file was shared with, who hears when that ends
    /// (sections 8 and 9).
    /// </summary>
    public bool Names(Guid fileId) => rowsByFile.TryGetValue(fileId, out var rows) && rows.Count > 0;

    private IEnumerable<DriveAclIndexRecord> Live(Guid fileId) =>
        rowsByFile.TryGetValue(fileId, out var rows) ? rows.Where(r => TableDriveAclIndex.IsLive(r, now)) : [];
}
