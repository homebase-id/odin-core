#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Hosting.Controllers.OwnerToken.AppManagement;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.AppManagement;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.DriveManagement;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Auth;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.Peer.Encryption;
using Odin.Services.Security.ZeroKnowledge;
using Odin.Test.Helpers;

using static Odin.Hosting.Tests.V2.ZeroKnowledge.ZkAppSteps;
using static Odin.Hosting.Tests.V2.ZeroKnowledge.ZkTestFiles;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// A drive whose app asks for it Private or Selective Share is created zero knowledge, with keys made in the owner's
/// browser, and never converted (docs/zero-knowledge-drive-api-plan.md, step 4).  The test plays the browser.
/// </summary>
[TestFixture]
public class ZkDriveCreationTests : V2Fixture
{
    private const string Content = "synced";

    [Test]
    public async Task ADriveAskedForAsSelectiveShareIsCreatedWithNoKeyTheServerCanOpen()
    {
        var (frodo, zkMasterKey) = await OwnerWithZeroKnowledgeKeysAsync();
        var drive = TargetDrive.NewTargetDrive();
        var appId = Guid.NewGuid();
        await frodo.Admin.EnsureAppRegistered(appId);
        var keys = ZkBrowser.MakeDriveKeys(drive.Alias, zkMasterKey);

        await frodo.Admin.CreateDrive(drive, "Synced", allowAnonymousReads: false, appId: appId, driveSlug: new OdinSlug("synced"),
            sharingModel: DriveSharingModel.SelectiveShare, zkKeys: keys.Request);

        var created = await frodo.Admin.GetDrive(drive);
        Assert.That(created.SharingModel, Is.EqualTo(DriveSharingModel.SelectiveShare));
        Assert.That(ZkBrowser.Open(created.ZkMasterKeyEncryptedStorageKey, zkMasterKey), Is.EqualTo(keys.DriveKey));
        Assert.That(created.WriteOnlyPublicKeyJwk, Is.EqualTo(keys.WriteOnlyPublicKeyJwk), "the browser's key pair, not one the server made");
        Assert.That((await frodo.Admin.GetAppDrive(appId, "synced")).SharingModel, Is.EqualTo(DriveSharingModel.SelectiveShare),
            "the app sees what it got");

        // The drive takes only its own key headers from the start
        var e = Assert.ThrowsAsync<Exception>(() => frodo.Drives.Writer.CreateEncryptedFile(drive.Alias, Metadata(Guid.NewGuid(), Content),
            transitOptions: null!));
        Assert.That(e!.Message, Does.EndWith(nameof(OdinClientErrorCode.DriveIsZeroKnowledge)));
        var fileKey = KeyHeader.NewRandom16();
        var (upload, _, _, _) = await frodo.Drives.Writer.CreateEncryptedFile(drive.Alias, Metadata(Guid.NewGuid(), Content),
            transitOptions: null!, keyHeader: fileKey, lockedKeyHeader: ZkBrowser.LockFileKey(fileKey, keys.DriveKey));
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK), upload.Error?.Content);

        // An app granted it gets no key on its grant; a device that signs in with the app's key opens the drive's key
        var app = await AppSession.SetupAsync(frodo, drive, DrivePermission.ReadWrite);
        Assert.That((await GrantOnAsync(app, drive)).HasStorageKey, Is.False);
        var (device, appKey) = await SignInAsync(frodo, app.AppId, zkMasterKey);
        Assert.That(ZkBrowser.Open((await GrantOnAsync(device, drive)).ZkAppKeyEncryptedStorageKey!, appKey), Is.EqualTo(keys.DriveKey));
    }

    [Test]
    public async Task ADriveAskedForWithNoModelIsCollaborativeAsToday()
    {
        var (frodo, _) = await OwnerWithZeroKnowledgeKeysAsync();
        var drive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(drive, "Shared");

        var created = await frodo.Admin.GetDrive(drive);
        Assert.That(created.SharingModel, Is.EqualTo(DriveSharingModel.Collaborative));
        Assert.That(created.ZkMasterKeyEncryptedStorageKey, Is.Null);
    }

    [Test]
    public async Task ARequestWhoseModelAndKeysDisagreeIsRefusedAndCreatesNothing()
    {
        var (frodo, zkMasterKey) = await OwnerWithZeroKnowledgeKeysAsync();

        await AssertRefusedAsync(frodo, DriveSharingModel.Private, withKeys: false, zkMasterKey, "no keys");
        await AssertRefusedAsync(frodo, DriveSharingModel.Collaborative, withKeys: true, zkMasterKey, "keys for a shared drive");
        await AssertRefusedAsync(frodo, DriveSharingModel.Unclassified, withKeys: false, zkMasterKey, "no such model to ask for");
        await AssertRefusedAsync(frodo, DriveSharingModel.SelectiveShare, withKeys: true, zkMasterKey, "anonymous reads",
            allowAnonymousReads: true);
        await AssertRefusedAsync(frodo, DriveSharingModel.Private, withKeys: true, zkMasterKey, "the CDN", allowCdn: true);
        await AssertRefusedAsync(frodo, DriveSharingModel.Private, withKeys: true, zkMasterKey, "subscriptions",
            allowSubscriptions: true);
    }

    [Test]
    public async Task ADriveAskedForAsPrivateIsCreatedPrivate()
    {
        var (frodo, zkMasterKey) = await OwnerWithZeroKnowledgeKeysAsync();
        var drive = TargetDrive.NewTargetDrive();
        var keys = ZkBrowser.MakeDriveKeys(drive.Alias, zkMasterKey);

        await frodo.Admin.CreateDrive(drive, "Kept", allowAnonymousReads: false, sharingModel: DriveSharingModel.Private,
            zkKeys: keys.Request);

        var created = await frodo.Admin.GetDrive(drive);
        Assert.That(created.SharingModel, Is.EqualTo(DriveSharingModel.Private));
        Assert.That(ZkBrowser.Open(created.ZkMasterKeyEncryptedStorageKey, zkMasterKey), Is.EqualTo(keys.DriveKey));
    }

    [Test]
    public async Task AZeroKnowledgeDriveNeedsTheOwnersZeroKnowledgeKeysFirst()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        await AssertRefusedAsync(frodo, DriveSharingModel.Private, withKeys: true, ByteArrayUtil.GetRndByteArray(16),
            "no zero-knowledge keys yet", expected: OdinClientErrorCode.ZeroKnowledgeKeysNotSet);
    }

    [Test]
    public async Task TheDeviceListSaysWhichDevicesHoldTheAppsKey()
    {
        var (frodo, zkMasterKey) = await OwnerWithZeroKnowledgeKeysAsync();
        var olderDevice = await AppSession.SetupAsync(frodo, Vault, DrivePermission.ReadWrite);
        await SignInAsync(frodo, olderDevice.AppId, zkMasterKey);

        var clients = await frodo.RefitFor<IRefitAppRegistration>().GetRegisteredClients(new GetAppRequest { AppId = olderDevice.AppId });
        Assert.That(clients.StatusCode, Is.EqualTo(HttpStatusCode.OK), clients.Error?.Content);
        Assert.That(clients.Content!.Select(c => c.HoldsZkAppKey).OrderBy(h => h), Is.EqualTo(new[] { false, true }),
            "the device that signed in with the app's key, and the one from before");
    }

    private static async Task AssertRefusedAsync(OwnerSession owner, DriveSharingModel model, bool withKeys, byte[] zkMasterKey,
        string why, bool allowAnonymousReads = false, bool allowCdn = false, bool allowSubscriptions = false,
        OdinClientErrorCode expected = OdinClientErrorCode.ArgumentError)
    {
        var drive = TargetDrive.NewTargetDrive();
        var response = await owner.Admin.TryCreateDrive(drive, "Refused", allowAnonymousReads: allowAnonymousReads,
            allowSubscriptions: allowSubscriptions, allowCdn: allowCdn, sharingModel: model,
            zkKeys: withKeys ? ZkBrowser.MakeDriveKeys(drive.Alias, zkMasterKey).Request : null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), $"{why}: {response.Error?.Content}");
        Assert.That(response.Error!.Content, Does.Contain(expected.ToString()).IgnoreCase, why);
        Assert.That((await owner.Admin.GetDrives()).Any(d => d.TargetDriveInfo == drive), Is.False, $"{why}: nothing created");
    }

    private async Task<(OwnerSession Owner, byte[] ZkMasterKey)> OwnerWithZeroKnowledgeKeysAsync()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var (zkMasterKey, _, _) = await ZkOwnerSteps.MakeZeroKnowledgeAsync(frodo, OwnerLogin.DefaultPassword);
        return (frodo, zkMasterKey);
    }
}
