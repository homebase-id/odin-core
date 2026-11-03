#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Membership.Circles;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// Sharing with a group (file-access.md, "Sharing with a group"): the app's circle for the group grants its drive, and a
/// file shared with it has one row, which every member reads through; a member's new file can carry that row, so it
/// goes back to the whole group.  And a member's file sent again, as on a community drive (file-access.md, "With
/// Selective Share").
/// </summary>
public partial class SelectiveShareTests
{
    [Test]
    public async Task AGroupReadsAFileThroughItsOneRow()
    {
        var s = await ArrangeAsync();
        var pippin = await LoginAsOwner(Identities.Pippin);
        var group = await GroupAsync(s, Identities.Sam, Identities.Pippin);

        var fileId = await s.UploadAsync(SecurityGroupType.Owner, [s.Share(Guid.Empty, DrivePermission.Read, circleId: group)]);
        Assert.That((await s.SharesAsync(fileId)).Select(r => r.CircleId), Is.EqualTo(new[] { group }), "one row, the group's");
        await AssertSamSeesAsync(s, fileId, true, "Sam, in the group");
        await AssertSeesAsync(pippin, s, fileId, true, "Pippin, in the group and no other circle of the app");

        var revoke = await s.Frodo.Connections.RevokeCircle(group, pippin.Identity);
        Assert.That(revoke.StatusCode, Is.EqualTo(HttpStatusCode.OK), revoke.Error?.Content);
        await AssertSeesAsync(pippin, s, fileId, false, "Pippin, out of the group", hasDriveAccess: false);
        await AssertSamSeesAsync(s, fileId, true, "Sam, still in it");
    }

    [Test]
    public async Task AMembersFileGoesBackToTheGroupAndStaysTheirs()
    {
        var s = await ArrangeAsync();
        var pippin = await LoginAsOwner(Identities.Pippin);
        var group = await GroupAsync(s, Identities.Sam, Identities.Pippin);

        // Pippin, whom only the group grants the drive, adds a file carrying the group's row
        var uniqueId = Guid.NewGuid();
        await ZkTestFiles.SendDepositAsync(pippin, pippin, s.Frodo, s.WriteOnlyPublicKeyJwk, uniqueId, "Pippin's file", remoteDrive: s.Drive,
            shares: [s.Share(Guid.Empty, DrivePermission.Read | DrivePermission.Write, lockedFileKey: LockedFileKey(), circleId: group)]);
        var sent = await s.HeaderByUniqueIdAsync(uniqueId);
        Assert.That((await s.SharesAsync(sent.FileId)).Select(r => r.CircleId), Is.EqualTo(new[] { group }), "the group's row");
        await AssertSamSeesAsync(s, sent.FileId, true, "Sam, through the group");
        await AssertSeesAsync(pippin, s, sent.FileId, true, "Pippin, his own");

        // Sam holds Write on it through the group, but sending it again is its original sender's, as on a community drive
        await ZkTestFiles.SendDepositAsync(s.Sam, s.Sam, s.Frodo, s.WriteOnlyPublicKeyJwk, uniqueId, "Sam's take", remoteDrive: s.Drive);
        Assert.That((await s.HeaderAsync(sent.FileId)).FileMetadata.VersionTag, Is.EqualTo(sent.FileMetadata.VersionTag),
            "unchanged by Sam's send");

        // Pippin's own send again changes it, and the group still reads it
        await ZkTestFiles.SendDepositAsync(pippin, pippin, s.Frodo, s.WriteOnlyPublicKeyJwk, uniqueId, "Pippin's file, again", remoteDrive: s.Drive);
        var after = await s.HeaderAsync(sent.FileId);
        Assert.That(after.FileMetadata.VersionTag, Is.Not.EqualTo(sent.FileMetadata.VersionTag), "Pippin's send changed it");
        Assert.That(after.FileMetadata.AppData.Content, Is.Not.EqualTo(sent.FileMetadata.AppData.Content), "with his new content");
        Assert.That(after.ServerMetadata.AccessControlList.RequiredSecurityGroup, Is.EqualTo(SecurityGroupType.Connected), "still shared");
        await AssertSamSeesAsync(s, sent.FileId, true, "Sam, after Pippin's change");
    }

    [TestCase(DrivePermission.Read | DrivePermission.Write, true, TestName = "AMembersFileSentAgainChangesItThroughAWriteShare")]
    [TestCase(DrivePermission.Read, false, TestName = "AMembersFileSentAgainWithoutAWriteShareIsRefused")]
    public async Task AMembersFileSentAgain(DrivePermission theirShare, bool changes)
    {
        // As on a community drive its sender changes it by sending it again, here through a live share with Write on it;
        // it keeps its shares either way, and the group that lets members read it
        var s = await ArrangeAsync();
        var uniqueId = Guid.NewGuid();
        await ZkTestFiles.SendDepositAsync(s.Sam, s.Sam, s.Frodo, s.WriteOnlyPublicKeyJwk, uniqueId, "Sam's file", remoteDrive: s.Drive,
            shares: [s.Share(Guid.Empty, theirShare, lockedFileKey: LockedFileKey())]);
        var before = await s.HeaderByUniqueIdAsync(uniqueId);

        // Shares come only with a new file
        await ZkTestFiles.SendDepositAsync(s.Sam, s.Sam, s.Frodo, s.WriteOnlyPublicKeyJwk, uniqueId, "sent again with shares", null, s.Drive,
            shares: [s.Share(Guid.Empty, DrivePermission.Read, lockedFileKey: LockedFileKey())]);
        Assert.That((await s.HeaderAsync(before.FileId)).FileMetadata.VersionTag, Is.EqualTo(before.FileMetadata.VersionTag),
            "refused: sent again with shares");

        await ZkTestFiles.SendDepositAsync(s.Sam, s.Sam, s.Frodo, s.WriteOnlyPublicKeyJwk, uniqueId, "sent again", null, s.Drive);

        var after = await s.HeaderAsync(before.FileId);
        Assert.That(after.FileMetadata.VersionTag,
            changes ? Is.Not.EqualTo(before.FileMetadata.VersionTag) : Is.EqualTo(before.FileMetadata.VersionTag),
            "changed only through a Write share");
        Assert.That(after.ServerMetadata.AccessControlList.RequiredSecurityGroup, Is.EqualTo(SecurityGroupType.Connected), "still shared");
        Assert.That((await s.SharesAsync(before.FileId)).Single().Permissions, Is.EqualTo(theirShare), "its share as it was");
        await AssertSamSeesAsync(s, after.FileId, true, "after sending it again");
    }

    /// <summary>The app makes a group of <paramref name="members"/>: its own circle granting Read and Write on the drive, its key set.</summary>
    private static Task<Guid> GroupAsync(Arranged s, params string[] members) =>
        s.AppCircleAsync(new CreateAppCircleRequest { Name = "the fellowship", DriveGrants = s.ReadWriteGrant().Drives }, members);
}
