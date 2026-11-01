#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Hosting.Controllers.OwnerToken.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.DriveManagement;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Auth;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;
using Odin.Test.Helpers;

using static Odin.Hosting.Tests.V2.ZeroKnowledge.ZkTestFiles;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// The header on a Selective Share drive (docs/selective-share-pr3-plan.md, section 1): the ACL names no one and is
/// Owner or Connected, and the drive is neither owner only nor a collaborative channel, at creation or later.  The
/// rules themselves are SelectiveShareDrivesTests'; these show each write path asks them.
/// </summary>
[TestFixture]
public class SelectiveShareHeaderTests : V2Fixture
{
    private static readonly Dictionary<string, string> CollaborativeChannel = new()
    {
        [BuiltInDriveAttributes.IsCollaborativeChannel] = "true"
    };

    [Test]
    public async Task ADriveIsCreatedNeitherOwnerOnlyNorACollaborativeChannel()
    {
        var (frodo, zkMasterKey) = await OwnerWithZeroKnowledgeKeysAsync();

        await AssertCreationRefusedAsync(frodo, zkMasterKey, "owner only", ownerOnly: true);
        await AssertCreationRefusedAsync(frodo, zkMasterKey, "a collaborative channel", attributes: CollaborativeChannel);
    }

    [Test]
    public async Task ADrivesAttributesCantMakeItACollaborativeChannel()
    {
        var (frodo, zkMasterKey) = await OwnerWithZeroKnowledgeKeysAsync();
        var (drive, _) = await CreateSelectiveShareDriveAsync(frodo, zkMasterKey);
        var svc = frodo.RefitFor<IRefitDriveManagement>();

        var refused = await svc.UpdateAttributes(new UpdateDriveDefinitionRequest { TargetDrive = drive, Attributes = CollaborativeChannel });
        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), refused.Error?.Content);
        Assert.That((await frodo.Admin.GetDrive(drive)).Attributes, Does.Not.ContainKey(BuiltInDriveAttributes.IsCollaborativeChannel),
            "nothing changed");

        var other = await svc.UpdateAttributes(new UpdateDriveDefinitionRequest
        {
            TargetDrive = drive, Attributes = new Dictionary<string, string> { ["a"] = "b" }
        });
        Assert.That(other.IsSuccessStatusCode, Is.True, $"any other attribute: {other.Error?.Content}");
    }

    [Test]
    public async Task AFileIsOwnerOrConnectedAndNamesNoOne()
    {
        var (frodo, zkMasterKey) = await OwnerWithZeroKnowledgeKeysAsync();
        var (drive, driveKey) = await CreateSelectiveShareDriveAsync(frodo, zkMasterKey);

        foreach (var group in new[] { SecurityGroupType.Owner, SecurityGroupType.Connected })
        {
            var (upload, _, _) = await UploadAsync(frodo, drive, driveKey, new AccessControlList { RequiredSecurityGroup = group });
            Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"{group}: {upload.Error?.Content}");
        }

        await AssertUploadRefusedAsync(frodo, drive, driveKey, "a lower rung",
            new AccessControlList { RequiredSecurityGroup = SecurityGroupType.AutoConnected });
        await AssertUploadRefusedAsync(frodo, drive, driveKey, "a named circle",
            new AccessControlList { RequiredSecurityGroup = SecurityGroupType.Connected, CircleIdList = [Guid.NewGuid()] });
        await AssertUploadRefusedAsync(frodo, drive, driveKey, "a named identity",
            new AccessControlList { RequiredSecurityGroup = SecurityGroupType.Connected, OdinIdList = [TestIdentities.Samwise.OdinId] });
    }

    [Test]
    public async Task AnUpdateCantNameAnyone()
    {
        var (frodo, zkMasterKey) = await OwnerWithZeroKnowledgeKeysAsync();
        var (drive, driveKey) = await CreateSelectiveShareDriveAsync(frodo, zkMasterKey);
        var (upload, uniqueId, fileKey) = await UploadAsync(frodo, drive, driveKey, AccessControlList.OwnerOnly);
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK), upload.Error?.Content);

        var update = await UpdateAsync(frodo, drive, driveKey, upload.Content!, uniqueId, fileKey,
            new AccessControlList { RequiredSecurityGroup = SecurityGroupType.Connected, CircleIdList = [Guid.NewGuid()] });
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), update.Error?.Content);
        Assert.That(update.Error!.Content, Does.Contain(nameof(OdinClientErrorCode.DriveSecurityAndAclMismatch)).IgnoreCase);

        var connected = await UpdateAsync(frodo, drive, driveKey, upload.Content!, uniqueId, fileKey,
            new AccessControlList { RequiredSecurityGroup = SecurityGroupType.Connected });
        Assert.That(connected.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"Connected, naming no one: {connected.Error?.Content}");
    }

    private static async Task AssertCreationRefusedAsync(OwnerSession owner, byte[] zkMasterKey, string why, bool ownerOnly = false,
        Dictionary<string, string>? attributes = null)
    {
        var drive = TargetDrive.NewTargetDrive();
        var response = await owner.Admin.TryCreateDrive(drive, "Refused", allowAnonymousReads: false, ownerOnly: ownerOnly,
            attributes: attributes, sharingModel: DriveSharingModel.SelectiveShare,
            zkKeys: ZkBrowser.MakeDriveKeys(drive.Alias, zkMasterKey).Request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), $"{why}: {response.Error?.Content}");
        Assert.That(response.Error!.Content, Does.Contain("SelectiveShare"), why);
    }

    private static async Task AssertUploadRefusedAsync(OwnerSession owner, TargetDrive drive, byte[] driveKey, string why,
        AccessControlList acl)
    {
        var e = Assert.ThrowsAsync<Exception>(() => UploadAsync(owner, drive, driveKey, acl), why);
        Assert.That(e!.Message, Does.EndWith(nameof(OdinClientErrorCode.DriveSecurityAndAclMismatch)), why);
    }

    private static async Task<(Refit.ApiResponse<CreateFileResult> Upload, Guid UniqueId, KeyHeader FileKey)> UploadAsync(
        OwnerSession owner, TargetDrive drive, byte[] driveKey, AccessControlList acl)
    {
        var uniqueId = Guid.NewGuid();
        var metadata = Metadata(uniqueId, "shared");
        metadata.AccessControlList = acl;
        var fileKey = KeyHeader.NewRandom16();
        var (upload, _, _, _) = await owner.Drives.Writer.CreateEncryptedFile(drive.Alias, metadata, transitOptions: null!,
            keyHeader: fileKey, lockedKeyHeader: ZkBrowser.LockFileKey(fileKey, driveKey));
        return (upload, uniqueId, fileKey);
    }

    private static Task<Refit.ApiResponse<UpdateFileResult>> UpdateAsync(OwnerSession owner, TargetDrive drive, byte[] driveKey,
        CreateFileResult created, Guid uniqueId, KeyHeader fileKey, AccessControlList acl)
    {
        var keyHeader = WithNewIv(fileKey);
        var metadata = Metadata(uniqueId, "updated", created.NewVersionTag);
        metadata.AccessControlList = acl;
        metadata.AppData.Content = keyHeader.EncryptDataAes(metadata.AppData.Content.ToUtf8ByteArray()).ToBase64();
        metadata.IsEncrypted = true;

        return owner.Drives.Writer.UpdateFileByFileId(drive.Alias, created.FileId, new FileUpdateInstructionSetV2
        {
            Locale = UpdateLocale.Local,
            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            Manifest = new UploadManifest { PayloadDescriptors = [] },
        }, metadata, [], ZkBrowser.LockFileKey(keyHeader, driveKey));
    }

    private static async Task<(TargetDrive Drive, byte[] DriveKey)> CreateSelectiveShareDriveAsync(OwnerSession owner, byte[] zkMasterKey)
    {
        var drive = TargetDrive.NewTargetDrive();
        var keys = ZkBrowser.MakeDriveKeys(drive.Alias, zkMasterKey);
        await owner.Admin.CreateDrive(drive, "Shared", allowAnonymousReads: false, sharingModel: DriveSharingModel.SelectiveShare,
            zkKeys: keys.Request);
        return (drive, keys.DriveKey);
    }

    private async Task<(OwnerSession Owner, byte[] ZkMasterKey)> OwnerWithZeroKnowledgeKeysAsync()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var (zkMasterKey, _, _) = await ZkOwnerSteps.MakeZeroKnowledgeAsync(frodo, OwnerLogin.DefaultPassword);
        return (frodo, zkMasterKey);
    }
}
