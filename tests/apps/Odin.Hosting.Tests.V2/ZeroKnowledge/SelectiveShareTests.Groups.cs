#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Membership.Circles;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// Sharing with a group (file-access.md, "Sharing with a group"): the app's circle for the group grants its drive, and a
/// file shared with it has one row, which every member reads through; a member's new file can carry that row, so it
/// goes back to the whole group.
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
        await ResetFrodosContextsAsync(s);
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
        var sent = (await s.Frodo.Drives.Reader.GetFileHeaderByUniqueIdAsync(uniqueId, s.Drive.Alias)).Content!;
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
        Assert.That((after.FileMetadata.VersionTag == sent.FileMetadata.VersionTag, after.FileMetadata.AppData.Content == sent.FileMetadata.AppData.Content),
            Is.EqualTo((false, false)), "Pippin's send changed it");
        Assert.That(after.ServerMetadata.AccessControlList.RequiredSecurityGroup, Is.EqualTo(SecurityGroupType.Connected), "still shared");
        await AssertSamSeesAsync(s, sent.FileId, true, "Sam, after Pippin's change");
    }

    /// <summary>The app makes a group of <paramref name="members"/>: its own circle granting Read and Write on the drive, its key set.</summary>
    private static async Task<Guid> GroupAsync(Arranged s, params string[] members)
    {
        var create = await s.App.RefitFor<IConnectionNetworkHttpClientApiV2>().CreateCircle(new CreateAppCircleRequest
        {
            Name = "the fellowship", DriveGrants = s.ReadWriteGrant().Drives
        });
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.OK), create.Error?.Content);
        await s.SetShareKeyAsync(create.Content);
        foreach (var member in members)
        {
            await AppCircles.AddMemberAsync(s.App, create.Content, member);
        }

        return create.Content;
    }
}
