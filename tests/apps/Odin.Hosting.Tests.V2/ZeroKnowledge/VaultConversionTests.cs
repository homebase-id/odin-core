#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Auth;
using Odin.Hosting.Tests.V2.YouAuth;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.AppManagement;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Peer.Encryption;
using Odin.Services.Util;

using static Odin.Hosting.Tests.V2.ZeroKnowledge.ZkAppSteps;
using static Odin.Hosting.Tests.V2.ZeroKnowledge.ZkTestFiles;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// Vault goes zero knowledge while it keeps its key, and an app's devices get its zero-knowledge key at sign-in
/// (docs/zero-knowledge-drive-api-plan.md, step 3).  The test plays the owner's browser and the app's device.
/// </summary>
[TestFixture]
public class VaultConversionTests : V2Fixture
{
    private const string Content = "what's in the vault";

    [Test]
    public async Task ConvertingVaultKeepsItsKeyAndOnlyRelabelsItsFiles()
    {
        var (frodo, zkMasterKey) = await OwnerWithZeroKnowledgeKeysAsync();
        var fileKey = KeyHeader.NewRandom16();
        var (encrypted, _, _, _) = await frodo.Drives.Writer.CreateEncryptedFile(VaultId, Metadata(Guid.NewGuid(), Content),
            transitOptions: null!, keyHeader: fileKey);
        Assert.That(encrypted.StatusCode, Is.EqualTo(HttpStatusCode.OK), encrypted.Error?.Content);
        var plain = await frodo.Drives.Writer.UploadNewMetadata(VaultId, Metadata(Guid.NewGuid(), "plain"));
        Assert.That(plain.StatusCode, Is.EqualTo(HttpStatusCode.OK), plain.Error?.Content);

        var before = await HeaderAsync(frodo, encrypted.Content!.FileId);
        Assert.That(before.SharedSecretEncryptedKeyHeader.Type, Is.EqualTo(EncryptionType.Aes), "the server locked it, before");

        var converted = await ConvertVaultAsync(frodo, zkMasterKey);

        // The same file key under the same IV, now as the drive key's own: nothing re-encrypted, nothing moved
        var after = await HeaderAsync(frodo, encrypted.Content.FileId);
        Assert.That(after.SharedSecretEncryptedKeyHeader.Type, Is.EqualTo(EncryptionType.ZkDrive));
        var opened = ZkBrowser.OpenFileKey(after.SharedSecretEncryptedKeyHeader, converted.DriveKey);
        Assert.That(opened.AesKey.GetKey(), Is.EqualTo(fileKey.AesKey.GetKey()), "the drive's own key opens the file key");
        Assert.That(opened.Decrypt(after.FileMetadata.AppData.Content.FromBase64()).ToStringFromUtf8Bytes(), Is.EqualTo(Content));
        Assert.That(after.FileMetadata.VersionTag, Is.EqualTo(before.FileMetadata.VersionTag), "the version tag doesn't move");
        Assert.That(after.FileMetadata.Updated, Is.EqualTo(before.FileMetadata.Updated), "nor does updated: no client resyncs");

        // An unencrypted file's empty key header stays aes, and the file stays as readable as it was
        var plainAfter = await HeaderAsync(frodo, plain.Content!.FileId);
        Assert.That(plainAfter.SharedSecretEncryptedKeyHeader.Type, Is.EqualTo(EncryptionType.Aes));
        Assert.That(plainAfter.FileMetadata.AppData.Content, Is.EqualTo("plain"));

        // The drive: private, its key only in the browser's copy
        var drive = (await frodo.Admin.GetDrives()).Single(d => d.TargetDriveInfo == Vault);
        Assert.That(drive.SharingModel, Is.EqualTo(DriveSharingModel.Private));
        Assert.That(ZkBrowser.Open(drive.ZkMasterKeyEncryptedStorageKey, zkMasterKey), Is.EqualTo(converted.DriveKey));

        // From now on the drive takes only its own key headers
        var newKey = KeyHeader.NewRandom16();
        var (zkUpload, _, _, _) = await frodo.Drives.Writer.CreateEncryptedFile(VaultId, Metadata(Guid.NewGuid(), Content),
            transitOptions: null!, keyHeader: newKey, lockedKeyHeader: ZkBrowser.LockFileKey(newKey, converted.DriveKey));
        Assert.That(zkUpload.StatusCode, Is.EqualTo(HttpStatusCode.OK), zkUpload.Error?.Content);
        var e = Assert.ThrowsAsync<Exception>(() => frodo.Drives.Writer.CreateEncryptedFile(VaultId, Metadata(Guid.NewGuid(), Content),
            transitOptions: null!));
        Assert.That(e!.Message, Does.EndWith(nameof(OdinClientErrorCode.DriveIsZeroKnowledge)));
    }

    [Test]
    public async Task ADeletedFilesTombstoneIsRelabelledWithTheRest()
    {
        // The tombstone keeps the real key header but not IsEncrypted; left aes, nobody could open it any more
        var (frodo, zkMasterKey) = await OwnerWithZeroKnowledgeKeysAsync();
        var fileKey = KeyHeader.NewRandom16();
        var (upload, _, _, _) = await frodo.Drives.Writer.CreateEncryptedFile(VaultId, Metadata(Guid.NewGuid(), Content),
            transitOptions: null!, keyHeader: fileKey);
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK), upload.Error?.Content);
        var delete = await frodo.Drives.Writer.SoftDeleteFile(VaultId, upload.Content!.FileId);
        Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.OK), delete.Error?.Content);

        var converted = await ConvertVaultAsync(frodo, zkMasterKey);

        var tombstone = await HeaderAsync(frodo, upload.Content.FileId);
        Assert.That(tombstone.SharedSecretEncryptedKeyHeader.Type, Is.EqualTo(EncryptionType.ZkDrive));
        Assert.That(ZkBrowser.OpenFileKey(tombstone.SharedSecretEncryptedKeyHeader, converted.DriveKey).AesKey.GetKey(),
            Is.EqualTo(fileKey.AesKey.GetKey()));
    }

    [Test]
    public async Task EditingTheAppsCirclesKeepsItsKeys()
    {
        // The registration is rebuilt field by field there; a field left out would strand the app's devices
        var (frodo, zkMasterKey) = await OwnerWithZeroKnowledgeKeysAsync();
        var app = await AppSession.SetupAsync(frodo, Vault, DrivePermission.ReadWrite);
        var converted = await ConvertVaultAsync(frodo, zkMasterKey);

        var update = await frodo.RefitFor<IRefitAppRegistration>().UpdateAuthorizedCircles(new UpdateAuthorizedCirclesRequest
        {
            AppId = app.AppId,
            AuthorizedCircles = [],
            CircleMemberPermissionGrant = new PermissionSetGrantRequest()
        });
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK), update.Error?.Content);

        var appKeys = (await GetAppKeysAsync(frodo)).Single(a => a.AppId == app.AppId);
        Assert.That(ZkBrowser.Open(appKeys.ZkMasterKeyEncryptedAppKey!, zkMasterKey), Is.EqualTo(converted.AppKeys[app.AppId]));
        Assert.That(appKeys.Drives.Single(d => d.DriveId == VaultId).HasAppCopy, Is.True);
    }

    /// <summary>
    /// Registering an app again would have replaced its keys and cut off its devices (#2039): it is refused, and a
    /// device signed in before still reads Vault.
    /// </summary>
    [Test]
    public async Task RegisteringAnAppAgainIsRefusedAndKeepsItsKeys()
    {
        var (frodo, zkMasterKey) = await OwnerWithZeroKnowledgeKeysAsync();
        var app = await AppSession.SetupAsync(frodo, Vault, DrivePermission.Read);
        var converted = await ConvertVaultAsync(frodo, zkMasterKey);
        var (device, appKey) = await SignInAsync(frodo, app.AppId, zkMasterKey);
        var uniqueId = Guid.NewGuid();
        var fileKey = KeyHeader.NewRandom16();
        var (upload, _, _, _) = await frodo.Drives.Writer.CreateEncryptedFile(VaultId, Metadata(uniqueId, Content),
            transitOptions: null!, keyHeader: fileKey, lockedKeyHeader: ZkBrowser.LockFileKey(fileKey, converted.DriveKey));
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK), upload.Error?.Content);
        var before = (await GetAppKeysAsync(frodo)).Single(a => a.AppId == app.AppId);

        var again = await frodo.Admin.TryRegisterApp(app.AppId, new PermissionSetGrantRequest
        {
            Drives = [new DriveGrantRequest { PermissionedDrive = new PermissionedDrive { Drive = Vault, Permission = DrivePermission.Read } }]
        });
        Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), again.Error?.Content);
        Assert.That(again.Error!.Content, Does.Contain(nameof(OdinClientErrorCode.AppAlreadyRegistered)).IgnoreCase);

        var after = (await GetAppKeysAsync(frodo)).Single(a => a.AppId == app.AppId);
        Assert.That(after.ZkMasterKeyEncryptedAppKey, Is.EqualTo(before.ZkMasterKeyEncryptedAppKey));
        Assert.That(after.Drives.Single(d => d.DriveId == VaultId).HasAppCopy, Is.True);
        Assert.That(ZkBrowser.Open((await GrantOnAsync(device, Vault)).ZkAppKeyEncryptedStorageKey!, appKey),
            Is.EqualTo(converted.DriveKey), "the device still opens its copy of Vault's key");
        var seen = await device.Drives.Reader.GetBatchAsync(VaultId, QueryFor(uniqueId));
        Assert.That(seen.StatusCode, Is.EqualTo(HttpStatusCode.OK), seen.Error?.Content);
        Assert.That(seen.Content!.SearchResults, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task EveryGrantOnVaultLosesItsKeyAndKeepsItsPermission()
    {
        var (frodo, zkMasterKey) = await OwnerWithZeroKnowledgeKeysAsync();
        var app = await AppSession.SetupAsync(frodo, Vault, DrivePermission.ReadWrite);
        Assert.That((await GrantOnAsync(app, Vault)).HasStorageKey, Is.True, "the grant carries the key, before");

        var converted = await ConvertVaultAsync(frodo, zkMasterKey);

        var grant = await GrantOnAsync(app, Vault);
        Assert.That(grant.HasStorageKey, Is.False, "no grant carries the key any more");
        Assert.That(grant.PermissionedDrive.Permission, Is.EqualTo(DrivePermission.ReadWrite), "the permission stays");

        // Any device of the app sees its locked copy: chat-kmp reads it as "this drive is zero knowledge"
        Assert.That(ZkBrowser.Open(grant.ZkAppKeyEncryptedStorageKey!, converted.AppKeys[app.AppId]), Is.EqualTo(converted.DriveKey));
        Assert.That(ZkBrowser.Open(grant.ZkStorageKeyEncryptedWriteOnlyPrivateKey!, converted.DriveKey),
            Is.EqualTo(converted.WriteOnlyPrivateKey));
    }

    [Test]
    public async Task ConvertingVaultClosesEverySocketButTheOwners()
    {
        // A socket keeps the context its client authenticated with: one from before the conversion has Vault's old
        // grants, the key the server no longer holds and no copy for the app, so every new Vault file event would be
        // held back from it.  The conversion closes it, subscribed yet or not, and the client reconnects.
        var (frodo, zkMasterKey) = await OwnerWithZeroKnowledgeKeysAsync();
        await AppSession.SetupAsync(frodo, Vault, DrivePermission.ReadWrite);
        var scope = frodo.Host.GetTenantScope(frodo.Identity.DomainName);
        var sockets = scope.Resolve<SharedDeviceSocketCollection<AppNotificationHandler>>();
        var dispatcher = scope.Resolve<AppNotificationDispatcher>();

        var ownerOnVault = Socket(VaultId, Context(masterKey: new SensitiveByteArray(Guid.NewGuid().ToByteArray())));
        var appOnVault = Socket(VaultId, Context());
        var appElsewhere = Socket(Guid.NewGuid(), Context());
        var notSubscribedYet = Socket(null, null);
        var all = new[] { ownerOnVault, appOnVault, appElsewhere, notSubscribedYet };
        foreach (var socket in all)
        {
            sockets.AddSocket(socket);
        }

        await dispatcher.SubscribeAsync();
        try
        {
            await ConvertVaultAsync(frodo, zkMasterKey);

            // Through the pub/sub channel, as on a node other than the one that converted
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (sockets.GetAll().ContainsKey(appOnVault.Key) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            var open = sockets.GetAll().Keys;
            Assert.That(open, Does.Contain(ownerOnVault.Key), "the owner's socket holds the master key, and stays");
            Assert.That(open, Does.Not.Contain(appOnVault.Key), "an app's socket on Vault");
            Assert.That(open, Does.Not.Contain(appElsewhere.Key), "an app's socket on another drive: its context has Vault's old grant too");
            Assert.That(open, Does.Not.Contain(notSubscribedYet.Key), "a socket that will subscribe with a context from before");
        }
        finally
        {
            await dispatcher.UnsubscribeAsync();
            foreach (var socket in all)
            {
                await sockets.RemoveSocket(socket.Key);
            }
        }
    }

    private static DeviceSocket Socket(Guid? driveId, IOdinContext? context) => new()
    {
        Key = Guid.NewGuid(),
        Drives = driveId == null ? [] : [driveId.Value],
        DeviceOdinContext = context
    };

    private static OdinContext Context(SensitiveByteArray? masterKey = null) => new()
    {
        Caller = new CallerContext((OdinId)Identities.Frodo, masterKey!, SecurityGroupType.Owner,
            masterKey == null ? new OdinClientContext { AppId = Guid.NewGuid() } : null!)
    };

    [Test]
    public async Task ConversionRefusesWithoutACopyForEveryAppGrantedVault()
    {
        var (frodo, zkMasterKey) = await OwnerWithZeroKnowledgeKeysAsync();
        var app = await AppSession.SetupAsync(frodo, Vault, DrivePermission.Read);

        var (request, _) = await MakeConversionAsync(frodo, zkMasterKey, leaveOut: appId => appId == app.AppId);
        var response = await ZkOwnerSteps.Client(frodo).ConvertDrive(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), response.Error?.Content);
        Assert.That(response.Error!.Content, Does.Contain(nameof(OdinClientErrorCode.ZkAppKeyMissing)).IgnoreCase);

        // Nothing happened: one transaction
        var drive = (await frodo.Admin.GetDrives()).Single(d => d.TargetDriveInfo == Vault);
        Assert.That(drive.SharingModel, Is.EqualTo(DriveSharingModel.Collaborative));
        Assert.That((await GrantOnAsync(app, Vault)).HasStorageKey, Is.True);
    }

    [Test]
    public async Task OnlyVaultConvertsThisWayAndOnlyOnce()
    {
        var (frodo, zkMasterKey) = await OwnerWithZeroKnowledgeKeysAsync();
        var client = ZkOwnerSteps.Client(frodo);

        var other = await client.GetDriveKeyForConversion(WellKnownAppDrives.ChatDrive.Alias.Value);
        Assert.That(other.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "another drive");
        Assert.That(other.Error!.Content, Does.Contain(nameof(OdinClientErrorCode.DriveCannotBeConverted)).IgnoreCase);

        await ConvertVaultAsync(frodo, zkMasterKey);
        var again = await client.GetDriveKeyForConversion(VaultId);
        Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "Vault, a second time: the server holds no key to hand over");
        Assert.That(again.Error!.Content, Does.Contain(nameof(OdinClientErrorCode.DriveCannotBeConverted)).IgnoreCase);
    }

    [Test]
    public async Task ADeviceSignedInWithTheAppKeyReadsAndWritesVaultAndAnOlderOneSeesNothing()
    {
        var (frodo, zkMasterKey) = await OwnerWithZeroKnowledgeKeysAsync();
        var olderDevice = await AppSession.SetupAsync(frodo, Vault, DrivePermission.ReadWrite);
        var uniqueId = Guid.NewGuid();
        var fileKey = KeyHeader.NewRandom16();
        var (upload, _, _, _) = await frodo.Drives.Writer.CreateEncryptedFile(VaultId, Metadata(uniqueId, Content),
            transitOptions: null!, keyHeader: fileKey);
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK), upload.Error?.Content);

        var converted = await ConvertVaultAsync(frodo, zkMasterKey);
        var (device, appKey) = await SignInAsync(frodo, olderDevice.AppId, zkMasterKey);
        Assert.That(appKey, Is.EqualTo(converted.AppKeys[olderDevice.AppId]), "the key the conversion locked Vault's key with");

        // The device opens the drive key from its context, and with it the file
        var driveKey = ZkBrowser.Open((await GrantOnAsync(device, Vault)).ZkAppKeyEncryptedStorageKey!, appKey);
        var batch = await device.Drives.Reader.GetBatchAsync(VaultId, QueryFor(uniqueId));
        Assert.That(batch.StatusCode, Is.EqualTo(HttpStatusCode.OK), batch.Error?.Content);
        var header = batch.Content!.SearchResults.Single();
        Assert.That(header.SharedSecretEncryptedKeyHeader.Type, Is.EqualTo(EncryptionType.ZkDrive));
        Assert.That(ZkBrowser.OpenFileKey(header.SharedSecretEncryptedKeyHeader, driveKey).AesKey.GetKey(), Is.EqualTo(fileKey.AesKey.GetKey()));

        // And writes the drive's own key headers, which the owner opens
        var newKey = KeyHeader.NewRandom16();
        var (written, _, _, _) = await device.Drives.Writer.CreateEncryptedFile(VaultId, Metadata(Guid.NewGuid(), Content),
            transitOptions: null!, keyHeader: newKey, lockedKeyHeader: ZkBrowser.LockFileKey(newKey, driveKey));
        Assert.That(written.StatusCode, Is.EqualTo(HttpStatusCode.OK), written.Error?.Content);
        var ownerRead = await HeaderAsync(frodo, written.Content!.FileId);
        Assert.That(ZkBrowser.OpenFileKey(ownerRead.SharedSecretEncryptedKeyHeader, converted.DriveKey).AesKey.GetKey(),
            Is.EqualTo(newKey.AesKey.GetKey()));

        // A device of the same app that never got the key sees the drive as empty, and can't read by id
        var olderBatch = await olderDevice.Drives.Reader.GetBatchAsync(VaultId, QueryFor(uniqueId));
        Assert.That(olderBatch.StatusCode, Is.EqualTo(HttpStatusCode.OK), olderBatch.Error?.Content);
        Assert.That(olderBatch.Content!.SearchResults, Is.Empty, "an older build must not be handed a header it can't open");
        var byId = await olderDevice.Drives.Reader.GetFileHeaderAsync(VaultId, upload.Content!.FileId);
        Assert.That(byId.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), byId.Error?.Content);
    }

    [Test]
    public async Task AnAppThatAsksForItsKeyGoesThroughConsentAndConsentMustSealItToTheSignIn()
    {
        var (frodo, zkMasterKey) = await OwnerWithZeroKnowledgeKeysAsync();
        var app = await AppSession.SetupAsync(frodo, Vault, DrivePermission.Read);
        var keyPair = YouAuthFlow.NewKeyPair();
        var request = SignInRequest(app.AppId, keyPair);
        var returnUrl = YouAuthFlow.AssertRedirectsToConsent(await YouAuthFlow.AuthorizeAsync(frodo.Host, frodo, request));

        // Consent without the key, or with it sealed to another key than the sign-in's, is refused
        await YouAuthFlow.GiveConsentAsync(frodo.Host, frodo, returnUrl, expected: HttpStatusCode.BadRequest);
        var (appKey, locked) = await BackfillAsync(frodo, app.AppId, zkMasterKey);
        var otherKey = ZkBrowser.MakeKeyPair().PublicKeyJwk;
        await YouAuthFlow.GiveConsentAsync(frodo.Host, frodo, returnUrl, ZkBrowser.Seal(appKey, otherKey), locked, HttpStatusCode.BadRequest);

        // So is consent naming a locked key that isn't the app's: a concurrent sign-in saved another first (here the
        // same key locked again, under another IV, which the server can't tell from a different key)
        await YouAuthFlow.GiveConsentAsync(frodo.Host, frodo, returnUrl, SealToSignIn(appKey, keyPair), ZkBrowser.Lock(appKey, zkMasterKey),
            HttpStatusCode.BadRequest);

        // A sign-in whose public key isn't one: a 400 at consent, not a 500
        var malformed = SignInRequest(app.AppId, keyPair);
        malformed.PublicKey = "not-a-key";
        var malformedReturn = YouAuthFlow.AssertRedirectsToConsent(await YouAuthFlow.AuthorizeAsync(frodo.Host, frodo, malformed));
        await YouAuthFlow.GiveConsentAsync(frodo.Host, frodo, malformedReturn, SealToSignIn(appKey, keyPair), locked, HttpStatusCode.BadRequest);

        // Still at the consent page: nothing lets a sign-in that asked for the key skip it
        YouAuthFlow.AssertRedirectsToConsent(await YouAuthFlow.AuthorizeAsync(frodo.Host, frodo, request));

        // A sign-in that doesn't ask gets no key, and its device isn't a key holder
        var plainKeyPair = YouAuthFlow.NewKeyPair();
        var plainRequest = SignInRequest(app.AppId, plainKeyPair, wantsZkAppKey: false);
        var plainReturn = YouAuthFlow.AssertRedirectsToConsent(await YouAuthFlow.AuthorizeAsync(frodo.Host, frodo, plainRequest));
        await YouAuthFlow.GiveConsentAsync(frodo.Host, frodo, plainReturn);
        var (_, token) = await CompleteSignInAsync(frodo, app.AppId, plainKeyPair, plainRequest);
        Assert.That(token.ZkAppKey, Is.Null);
    }

    [Test]
    public async Task TheConsentPageBackfillsTheCopyOfADriveConvertedBeforeTheAppHadAKey()
    {
        // The Shard Recovery drive went zero knowledge before apps had keys; an app granted it gets its copy at sign-in
        var frodo = await LoginAsOwner(Identities.Frodo);
        var (zkMasterKey, _, shardDriveKey) = await ZkOwnerSteps.MakeZeroKnowledgeAsync(frodo, OwnerLogin.DefaultPassword);
        var app = await AppSession.SetupAsync(frodo, WellKnownAppDrives.ShardRecoveryDrive, DrivePermission.Read);
        var before = (await GetAppKeysAsync(frodo)).Single(a => a.AppId == app.AppId);
        Assert.That(before.ZkMasterKeyEncryptedAppKey, Is.Null);
        Assert.That(before.Drives.Single(d => d.DriveId == WellKnownAppDrives.ShardRecoveryDrive.Alias.Value).HasAppCopy, Is.False);

        var (device, appKey) = await SignInAsync(frodo, app.AppId, zkMasterKey);

        var grant = await GrantOnAsync(device, WellKnownAppDrives.ShardRecoveryDrive);
        Assert.That(ZkBrowser.Open(grant.ZkAppKeyEncryptedStorageKey!, appKey), Is.EqualTo(shardDriveKey));
        var after = (await GetAppKeysAsync(frodo)).Single(a => a.AppId == app.AppId);
        Assert.That(after.Drives.Single(d => d.DriveId == WellKnownAppDrives.ShardRecoveryDrive.Alias.Value).HasAppCopy, Is.True);
    }

    [Test]
    public async Task AnAppsKeyIsSetOnceAndLocksOnlyTheZeroKnowledgeDrivesItIsGranted()
    {
        var (frodo, zkMasterKey) = await OwnerWithZeroKnowledgeKeysAsync();
        var app = await AppSession.SetupAsync(frodo, Vault, DrivePermission.Read);
        var (appKey, _) = await BackfillAsync(frodo, app.AppId, zkMasterKey);
        var client = ZkOwnerSteps.Client(frodo);

        // Replacing the key would strand every copy locked with it
        var replace = await client.SaveAppKeys(new ZkAppKeysRequest
        {
            AppId = app.AppId,
            ZkMasterKeyEncryptedAppKey = ZkBrowser.Lock(ByteArrayUtil.GetRndByteArray(16), zkMasterKey)
        });
        Assert.That(replace.Error?.Content, Does.Contain(nameof(OdinClientErrorCode.ZkAppKeyAlreadySet)).IgnoreCase);

        // A drive it isn't granted, and a drive that isn't zero knowledge (Vault, before conversion)
        var locked = ZkBrowser.Lock(ByteArrayUtil.GetRndByteArray(16), appKey);
        var notGranted = await client.SaveAppKeys(new ZkAppKeysRequest
        {
            AppId = app.AppId,
            ZkAppKeyEncryptedStorageKeys = { [WellKnownAppDrives.ShardRecoveryDrive.Alias.Value] = locked }
        });
        Assert.That(notGranted.Error?.Content, Does.Contain(nameof(OdinClientErrorCode.ZkDriveNotGrantedToApp)).IgnoreCase);
        var notZk = await client.SaveAppKeys(new ZkAppKeysRequest
        {
            AppId = app.AppId,
            ZkAppKeyEncryptedStorageKeys = { [VaultId] = locked }
        });
        Assert.That(notZk.Error?.Content, Does.Contain(nameof(OdinClientErrorCode.ZkDriveNotGrantedToApp)).IgnoreCase);
    }

    [Test]
    public async Task AWriteOnlyGrantGetsNoCopyOfTheKeyAndConversionNeedsNone()
    {
        // Writing alone deposits, sealed to the drive's public key: it never needed the key, before or after
        var (frodo, zkMasterKey) = await OwnerWithZeroKnowledgeKeysAsync();
        var writer = await AppSession.SetupAsync(frodo, Vault, DrivePermission.Write);
        Assert.That((await GetAppKeysAsync(frodo)).Single(a => a.AppId == writer.AppId).Drives, Is.Empty);

        await ConvertVaultAsync(frodo, zkMasterKey);

        var grant = await GrantOnAsync(writer, Vault);
        Assert.That(grant.PermissionedDrive.Permission, Is.EqualTo(DrivePermission.Write));
        Assert.That(grant.ZkAppKeyEncryptedStorageKey, Is.Null);
        var save = await ZkOwnerSteps.Client(frodo).SaveAppKeys(new ZkAppKeysRequest
        {
            AppId = writer.AppId,
            ZkMasterKeyEncryptedAppKey = ZkBrowser.Lock(ByteArrayUtil.GetRndByteArray(16), zkMasterKey),
            ZkAppKeyEncryptedStorageKeys = { [VaultId] = ZkBrowser.Lock(ByteArrayUtil.GetRndByteArray(16), zkMasterKey) }
        });
        Assert.That(save.Error?.Content, Does.Contain(nameof(OdinClientErrorCode.ZkDriveNotGrantedToApp)).IgnoreCase);
    }

    [Test]
    public async Task AnAppsCopyGoesWithItsGrantAndAGrantGivenAgainNeedsANewOne()
    {
        var (frodo, zkMasterKey) = await OwnerWithZeroKnowledgeKeysAsync();
        var app = await AppSession.SetupAsync(frodo, Vault, DrivePermission.Read);
        await ConvertVaultAsync(frodo, zkMasterKey);

        await UpdateVaultPermissionAsync(frodo, app.AppId, null);
        await UpdateVaultPermissionAsync(frodo, app.AppId, DrivePermission.Read);

        var vault = (await GetAppKeysAsync(frodo)).Single(a => a.AppId == app.AppId).Drives.Single(d => d.DriveId == VaultId);
        Assert.That(vault.HasAppCopy, Is.False, "the backfill makes a fresh copy");
        Assert.That((await GrantOnAsync(app, Vault)).ZkAppKeyEncryptedStorageKey, Is.Null);
    }

    [Test]
    public async Task MalformedKeyRequestsAreClientErrors()
    {
        var (frodo, zkMasterKey) = await OwnerWithZeroKnowledgeKeysAsync();
        var app = await AppSession.SetupAsync(frodo, Vault, DrivePermission.Read);
        var client = ZkOwnerSteps.Client(frodo);

        // Refused before the request is read (the response comes back encrypted, so only its status is checked here)
        var noCopies = await client.SaveAppKeys(new ZkAppKeysRequest { AppId = app.AppId, ZkAppKeyEncryptedStorageKeys = null! });
        Assert.That(noCopies.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), noCopies.Error?.Content);

        var (request, _) = await MakeConversionAsync(frodo, zkMasterKey);
        request.Apps.Add(request.Apps.Single(a => a.AppId == app.AppId));
        AssertArgumentError(await client.ConvertDrive(request));

        request.Apps = null!;
        var noApps = await client.ConvertDrive(request);
        Assert.That(noApps.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), noApps.Error?.Content);
    }

    [Test]
    public async Task AConversionWhoseWriteOnlyKeyIsntLockedWithTheDriveKeyIsRefused()
    {
        // The server still holds the drive key: the one thing it can check before there is no way back
        var (frodo, zkMasterKey) = await OwnerWithZeroKnowledgeKeysAsync();
        var (request, converted) = await MakeConversionAsync(frodo, zkMasterKey);
        request.ZkStorageKeyEncryptedWriteOnlyPrivateKey = ZkBrowser.Lock(converted.WriteOnlyPrivateKey, ByteArrayUtil.GetRndByteArray(16));
        AssertArgumentError(await ZkOwnerSteps.Client(frodo).ConvertDrive(request));

        var drive = (await frodo.Admin.GetDrives()).Single(d => d.TargetDriveInfo == Vault);
        Assert.That(drive.SharingModel, Is.EqualTo(DriveSharingModel.Collaborative), "nothing happened");

        // Nor a write-only key whose public half isn't the one sent
        (request, converted) = await MakeConversionAsync(frodo, zkMasterKey);
        request.WriteOnlyPublicKeyJwk = ZkBrowser.MakeKeyPair().PublicKeyJwk;
        AssertArgumentError(await ZkOwnerSteps.Client(frodo).ConvertDrive(request));

        // Nor anything locked with the drive key that isn't an EC private key: a 400, not a 500
        (request, converted) = await MakeConversionAsync(frodo, zkMasterKey);
        request.ZkStorageKeyEncryptedWriteOnlyPrivateKey = ZkBrowser.Lock(ByteArrayUtil.GetRndByteArray(100), converted.DriveKey);
        AssertArgumentError(await ZkOwnerSteps.Client(frodo).ConvertDrive(request));
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        request.ZkStorageKeyEncryptedWriteOnlyPrivateKey = ZkBrowser.Lock(rsa.ExportPkcs8PrivateKey(), converted.DriveKey);
        AssertArgumentError(await ZkOwnerSteps.Client(frodo).ConvertDrive(request));
        using var p256 = System.Security.Cryptography.ECDiffieHellman.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        request.ZkStorageKeyEncryptedWriteOnlyPrivateKey = ZkBrowser.Lock(p256.ExportPkcs8PrivateKey(), converted.DriveKey);
        AssertArgumentError(await ZkOwnerSteps.Client(frodo).ConvertDrive(request));
    }

    [Test]
    public async Task ADeviceSignsInByTheAppsSlugAsChatKmpDoes()
    {
        var (frodo, zkMasterKey) = await OwnerWithZeroKnowledgeKeysAsync();
        var app = await AppSession.SetupAsync(frodo, Vault, DrivePermission.Read);
        var converted = await ConvertVaultAsync(frodo, zkMasterKey);
        var slug = (await GetAppKeysAsync(frodo)).Single(a => a.AppId == app.AppId).AppSlug;

        var (device, appKey) = await SignInAsync(frodo, app.AppId, zkMasterKey, clientId: slug);

        Assert.That(appKey, Is.EqualTo(converted.AppKeys[app.AppId]));
        Assert.That(ZkBrowser.Open((await GrantOnAsync(device, Vault)).ZkAppKeyEncryptedStorageKey!, appKey), Is.EqualTo(converted.DriveKey));
    }

    [Test]
    public async Task AConversionCarriesOnlyTheConvertedDrivesCopies()
    {
        // Another drive's copy goes through the backfill, which checks it is zero knowledge
        var (frodo, zkMasterKey) = await OwnerWithZeroKnowledgeKeysAsync();
        var app = await AppSession.SetupAsync(frodo, new PermissionSetGrantRequest
        {
            Drives =
            [
                new DriveGrantRequest { PermissionedDrive = new PermissionedDrive { Drive = Vault, Permission = DrivePermission.Read } },
                new DriveGrantRequest { PermissionedDrive = new PermissionedDrive { Drive = WellKnownAppDrives.ShardRecoveryDrive, Permission = DrivePermission.Read } }
            ],
            PermissionSet = new PermissionSet()
        });

        var (request, converted) = await MakeConversionAsync(frodo, zkMasterKey);
        request.Apps.Single(a => a.AppId == app.AppId).ZkAppKeyEncryptedStorageKeys[WellKnownAppDrives.ShardRecoveryDrive.Alias.Value] =
            ZkBrowser.Lock(ByteArrayUtil.GetRndByteArray(16), converted.AppKeys[app.AppId]);
        AssertArgumentError(await ZkOwnerSteps.Client(frodo).ConvertDrive(request));
    }

    //

    private static void AssertArgumentError(Refit.IApiResponse response)
    {
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), response.Error?.Content);
        Assert.That(response.Error!.Content, Does.Contain(nameof(OdinClientErrorCode.ArgumentError)).IgnoreCase);
    }

    private static async Task UpdateVaultPermissionAsync(OwnerSession owner, Guid appId, DrivePermission? permission)
    {
        var response = await owner.RefitFor<IRefitAppRegistration>().UpdateAppPermissions(new UpdateAppPermissionsRequest
        {
            AppId = appId,
            PermissionSet = new PermissionSet(),
            Drives = permission == null
                ? []
                : [new DriveGrantRequest { PermissionedDrive = new PermissionedDrive { Drive = Vault, Permission = permission.Value } }]
        });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
    }

    private async Task<(OwnerSession Owner, byte[] ZkMasterKey)> OwnerWithZeroKnowledgeKeysAsync()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var (zkMasterKey, _, _) = await ZkOwnerSteps.MakeZeroKnowledgeAsync(frodo, OwnerLogin.DefaultPassword);
        return (frodo, zkMasterKey);
    }

    private static async Task<Odin.Services.Apps.SharedSecretEncryptedFileHeader> HeaderAsync(OwnerSession owner, Guid fileId)
    {
        var read = await owner.Drives.Reader.GetFileHeaderAsync(VaultId, fileId);
        Assert.That(read.StatusCode, Is.EqualTo(HttpStatusCode.OK), read.Error?.Content);
        return read.Content!;
    }
}
