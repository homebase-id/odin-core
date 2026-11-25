#nullable enable
using System;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Ported.Transit;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests.V2.Peer;

/// <summary>
/// The ACL a post on a drive marked as a community channel keeps when a member changes it, and the one a sent copy lands
/// with (#2002; docs/file-access.md, "Copies sent to others").
/// </summary>
[TestFixture]
public class CommunityChannelAclTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam, Identities.Pippin];

    [Test]
    public async Task AMembersChangeOfAPostKeepsItsAcl()
    {
        // Frodo narrows Pippin's post on his channel to Sam alone.  Sam's edit carries a copy of the ACL without the
        // names, and then Anonymous: his content lands both times, and the ACL that names him stays
        var (frodo, sam, pippin, drive) = await ChannelAsync();
        var post = await PostAsync(pippin, frodo, drive, AccessControlList.Connected);
        var posted = await HeaderAsync(frodo, drive, post.Gtid);

        var onlySam = new AccessControlList { RequiredSecurityGroup = SecurityGroupType.Connected, OdinIdList = [sam.Identity.DomainName] };
        await UpdateAsync(frodo, new FileIdentifier { FileId = posted.FileId, TargetDrive = drive }, posted.FileMetadata.VersionTag,
            "Frodo's, for Sam", onlySam, recipient: null);
        var narrowed = await HeaderAsync(frodo, drive, post.Gtid);
        Assert.That(narrowed.ServerMetadata.AccessControlList.OdinIdList, Is.EqualTo(new[] { sam.Identity.DomainName }));

        var samsEdit = await UpdateAsync(sam, OnPeer(drive, post), narrowed.FileMetadata.VersionTag, "Sam's",
            onlySam, frodo);
        var afterSam = await HeaderAsync(frodo, drive, post.Gtid);
        Assert.That(afterSam.FileMetadata.AppData.Content, Is.EqualTo(samsEdit), "Sam's edit landed");
        AssertOnlySam(afterSam, sam);

        var samsWider = await UpdateAsync(sam, OnPeer(drive, post), afterSam.FileMetadata.VersionTag, "Sam's, for anyone",
            AccessControlList.Anonymous, frodo);
        var afterWider = await HeaderAsync(frodo, drive, post.Gtid);
        Assert.That(afterWider.FileMetadata.AppData.Content, Is.EqualTo(samsWider), "Sam's edit landed");
        AssertOnlySam(afterWider, sam);
    }

    [Test]
    public async Task APostsSenderChangesItsAclOnlyToANarrowerOne()
    {
        // Pippin's own post: his change to Anonymous keeps it Connected, his change to Owner narrows it
        var (frodo, _, pippin, drive) = await ChannelAsync();
        var post = await PostAsync(pippin, frodo, drive, AccessControlList.Connected);
        var posted = await HeaderAsync(frodo, drive, post.Gtid);

        var wider = await UpdateAsync(pippin, OnPeer(drive, post), posted.FileMetadata.VersionTag, "Pippin's, for anyone",
            AccessControlList.Anonymous, frodo);
        var afterWider = await HeaderAsync(frodo, drive, post.Gtid);
        Assert.That(afterWider.FileMetadata.AppData.Content, Is.EqualTo(wider), "Pippin's edit landed");
        Assert.That(afterWider.ServerMetadata.AccessControlList.RequiredSecurityGroup, Is.EqualTo(SecurityGroupType.Connected));

        await UpdateAsync(pippin, OnPeer(drive, post), afterWider.FileMetadata.VersionTag, "Pippin's, for Frodo",
            AccessControlList.OwnerOnly, frodo);
        var afterNarrower = await HeaderAsync(frodo, drive, post.Gtid);
        Assert.That(afterNarrower.ServerMetadata.AccessControlList.RequiredSecurityGroup, Is.EqualTo(SecurityGroupType.Owner));
    }

    [Test]
    public async Task APostNamingOnlyIdentitiesLandsForTheChannelOwnerAlone()
    {
        // Pippin's post for Sam alone: the copy Frodo's channel gets can't name Sam, so it is Frodo's alone, not every
        // connection's
        var (frodo, sam, pippin, drive) = await ChannelAsync();
        var onlySam = new AccessControlList { RequiredSecurityGroup = SecurityGroupType.Connected, OdinIdList = [sam.Identity.DomainName] };
        var post = await PostAsync(pippin, frodo, drive, onlySam);

        var acl = (await HeaderAsync(frodo, drive, post.Gtid)).ServerMetadata.AccessControlList;
        Assert.That(acl.RequiredSecurityGroup, Is.EqualTo(SecurityGroupType.Owner));
        Assert.That(acl.GetRequiredIdentities(), Is.Empty);
    }

    private record Post(Guid Gtid);

    private static void AssertOnlySam(SharedSecretEncryptedFileHeader header, OwnerSession sam)
    {
        var acl = header.ServerMetadata.AccessControlList;
        Assert.That(acl.RequiredSecurityGroup, Is.EqualTo(SecurityGroupType.Connected));
        Assert.That(acl.OdinIdList, Is.EqualTo(new[] { sam.Identity.DomainName }), "the ACL still names Sam alone");
    }

    /// <summary>Frodo's channel, Sam and Pippin its members, Pippin with a copy of the drive to post from.</summary>
    private async Task<(OwnerSession Frodo, OwnerSession Sam, OwnerSession Pippin, TargetDrive Drive)> ChannelAsync()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var pippin = await LoginAsOwner(Identities.Pippin);
        var drive = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);
        await Ported.Peer.CollabChannelFlow.SetupAsync(frodo, drive, [pippin, sam]);
        await pippin.Admin.EnsureDrive(drive, $"{pippin.Identity} channel");
        return (frodo, sam, pippin, drive);
    }

    private static FileIdentifier OnPeer(TargetDrive drive, Post post) => new() { GlobalTransitId = post.Gtid, TargetDrive = drive };

    /// <summary>
    /// <paramref name="sender"/> posts an unencrypted file with <paramref name="acl"/> to
    /// <paramref name="channelOwner"/>'s channel; unencrypted, so its ACL may be anything, Anonymous included.
    /// </summary>
    private static async Task<Post> PostAsync(OwnerSession sender, OwnerSession channelOwner, TargetDrive drive, AccessControlList acl)
    {
        var metadata = SampleMetadataData.Create(fileType: 100, acl: acl);
        metadata.AllowDistribution = true;
        var send = await sender.V1.Drive.UploadNewMetadata(drive, metadata,
            new TransitOptions { Recipients = [channelOwner.Identity.DomainName] });
        Assert.That(send.IsSuccessStatusCode, Is.True, send.Error?.Content);
        await PeerFlow.DistributeAsync(sender, channelOwner, drive);
        return new Post(send.Content!.GlobalTransitId!.Value);
    }

    /// <summary>
    /// <paramref name="caller"/> updates <paramref name="file"/> with <paramref name="content"/> and <paramref name="acl"/>,
    /// naming <paramref name="versionTag"/>: a global transit id is <paramref name="recipient"/>'s copy, a file id the
    /// caller's own.  The content.
    /// </summary>
    private static async Task<string> UpdateAsync(OwnerSession caller, FileIdentifier file, Guid? versionTag, string content,
        AccessControlList acl, OwnerSession? recipient)
    {
        var metadata = SampleMetadataData.Create(fileType: 100, acl: acl);
        metadata.AllowDistribution = recipient != null;
        metadata.AppData.Content = content;
        metadata.VersionTag = versionTag;
        var update = await caller.V1.Drive.UpdateFile(new FileUpdateInstructionSet
        {
            Locale = file.GlobalTransitId.HasValue ? UpdateLocale.Peer : UpdateLocale.Local,
            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            File = file,
            Recipients = recipient == null ? [] : [recipient.Identity],
            Manifest = new UploadManifest { PayloadDescriptors = [] }
        }, metadata, []);
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK), update.Error?.Content);
        if (recipient != null)
        {
            await PeerFlow.DistributeAsync(caller, recipient, file.TargetDrive);
        }

        return content;
    }

    private static Task<SharedSecretEncryptedFileHeader> HeaderAsync(OwnerSession owner, TargetDrive drive, Guid gtid) =>
        TransitScenario.SingleByGlobalTransitIdAsync(owner, new GlobalTransitIdFileIdentifier { GlobalTransitId = gtid, TargetDrive = drive });
}
