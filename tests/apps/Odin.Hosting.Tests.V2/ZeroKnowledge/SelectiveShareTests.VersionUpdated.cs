#nullable enable
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Time;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// A share keeps the file's version tag, so it moves the file's <c>updated</c> and leaves its <c>versionUpdated</c>; a member
/// reads the owner's <c>versionUpdated</c> straight from the owner's server and through their own alike.
/// </summary>
public partial class SelectiveShareTests
{
    [Test]
    public async Task AShareLeavesVersionUpdatedAndAMemberReadsTheOwnersThroughEitherServer()
    {
        var s = await ArrangeAsync();
        var fileId = await s.UploadAsync(SecurityGroupType.Owner);
        var before = await s.HeaderAsync(fileId);
        Assert.That(before.FileMetadata.VersionUpdated, Is.EqualTo((UnixTimeUtc?)before.FileMetadata.Updated), "uploaded");

        await s.ShareAsync(fileId, DrivePermission.Read);

        var shared = await s.HeaderAsync(fileId);
        Assert.That((shared.FileMetadata.VersionTag, shared.FileMetadata.VersionUpdated),
            Is.EqualTo((before.FileMetadata.VersionTag, before.FileMetadata.VersionUpdated)), "the share kept (versionTag, versionUpdated)");
        Assert.That(shared.FileMetadata.Updated.milliseconds, Is.GreaterThan(before.FileMetadata.Updated.milliseconds), "and moved updated");

        var direct = await PeerTokenCaller.ForAsync(s.Sam, s.Frodo);
        var directHeader = await direct.Drives.Reader.GetFileHeaderAsync(s.Drive.Alias, fileId);
        Assert.That(directHeader.StatusCode, Is.EqualTo(HttpStatusCode.OK), directHeader.Error?.Content);
        var relayed = await s.Sam.Drives.Peer.GetFileHeaderAsync(s.Frodo.Identity, s.Drive.Alias, fileId);
        Assert.That(relayed.StatusCode, Is.EqualTo(HttpStatusCode.OK), relayed.Error?.Content);
        var relayedMinimal = await s.Sam.Drives.Peer.QueryBatchAsync(s.Frodo.Identity, s.Drive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { TargetDrive = s.Drive },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 100, ResultShape = FileResultShape.Minimal }
        });
        Assert.That(relayedMinimal.StatusCode, Is.EqualTo(HttpStatusCode.OK), relayedMinimal.Error?.Content);

        Assert.That((directHeader.Content!.FileMetadata.VersionUpdated, relayed.Content!.FileMetadata.VersionUpdated,
                relayedMinimal.Content!.FileRefs.Single(r => r.FileId == fileId).VersionUpdated),
            Is.EqualTo((before.FileMetadata.VersionUpdated, before.FileMetadata.VersionUpdated, before.FileMetadata.VersionUpdated)),
            "(direct header, relayed header, relayed minimal row) all the owner's versionUpdated");
    }
}
