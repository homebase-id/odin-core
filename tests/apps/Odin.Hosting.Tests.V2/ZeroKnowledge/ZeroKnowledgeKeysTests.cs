#nullable enable
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Hosting.Controllers.OwnerToken.Drive;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Security;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Auth;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Security.ZeroKnowledge;
using Refit;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// Plan A (docs/zero-knowledge-keys-plan.md): the owner's zero-knowledge keys and the Shard Recovery
/// drive's conversion.  <see cref="ZkBrowser"/> plays the browser, so these tests also pin the
/// formats the browser writes for every owner.
/// </summary>
[TestFixture]
public class ZeroKnowledgeKeysTests : V2Fixture
{
    private const string Password = OwnerLogin.DefaultPassword;
    private const string NewPassword = "672c~!!9402044";

    /// <summary>
    /// The same vector odin-js checks (js-lib ZeroKnowledgeKeys.test.ts), made with Python's
    /// hashlib and cryptography.  It pins the formats on both sides to each other.
    /// </summary>
    [Test]
    public void TheSharedVectorOpens()
    {
        var status = new ZkStatusResponse
        {
            ZkSaltKek64 = "AAECAwQFBgcICQoLDA0ODw==",
            ZkKekEncryptedZkMasterKey = "ICEiIyQlJicoKSorjWGICGzIuJPS3SPnEBWvlaM3EZ0jd9msGp8vJrhDYXA="
        };

        Assert.That(ZkBrowser.SecondPasswordKey("EnSøienØ", status.ZkSaltKek64).ToBase64(),
            Is.EqualTo("+pL3zwkIcDcDpYKkLbVc4A=="));
        Assert.That(ZkBrowser.OpenMasterKey("EnSøienØ", status).ToBase64(), Is.EqualTo("EBESExQVFhcYGRobHB0eHw=="));
        Assert.That(System.Guid.Parse("46242d0d67604b2aa683f05cd48d4aef").ToByteArray().ToBase64(),
            Is.EqualTo(System.Convert.ToBase64String(System.Convert.FromHexString("0d2d244660672a4ba683f05cd48d4aef"))),
            "the browser encrypts the drive id in .NET's Guid byte order");
    }

    [Test]
    public async Task StatusIsEmptyUntilTheBrowserMakesTheKeys()
    {
        var owner = await LoginAsOwner();

        var status = await GetStatus(owner);

        Assert.That(status.ZkSaltKek64, Is.Null);
        Assert.That(status.ZkKekEncryptedZkMasterKey, Is.Null);
        Assert.That((await GetShardRecoveryDrive(owner)).ZkMasterKeyEncryptedStorageKey, Is.Null);
    }

    [Test]
    public async Task TheStoredMasterKeyOpensWithThePassword()
    {
        var owner = await LoginAsOwner();
        var keys = await CreateKeys(owner);

        var status = await GetStatus(owner);

        Assert.That(ZkBrowser.OpenMasterKey(Password, status), Is.EqualTo(keys.MasterKey));
    }

    [Test]
    public async Task KeysAreNotReplacedOnceSet()
    {
        var owner = await LoginAsOwner();
        var keys = await CreateKeys(owner);

        var second = ZkBrowser.MakeKeys(Password);
        var response = await Client(owner).CreateKeys(second.Request);

        DriveAsserts.AssertRejected(response, OdinClientErrorCode.ZeroKnowledgeKeysAlreadySet);
        Assert.That(ZkBrowser.OpenMasterKey(Password, await GetStatus(owner)), Is.EqualTo(keys.MasterKey));
    }

    [Test]
    public async Task TwoFirstLoginsAtOnceSaveOneSetOfKeys()
    {
        var owner = await LoginAsOwner();
        var first = ZkBrowser.MakeKeys(Password);
        var second = ZkBrowser.MakeKeys(Password);

        var responses = await Task.WhenAll(
            Client(owner).CreateKeys(first.Request),
            Client(owner).CreateKeys(second.Request));

        var accepted = responses.Count(r => r.IsSuccessStatusCode);
        Assert.That(accepted, Is.EqualTo(1),
            string.Join(", ", responses.Select(r => $"{r.StatusCode}: {r.Error?.Content}")));
        var (winner, loser) = responses[0].IsSuccessStatusCode ? (first, responses[1]) : (second, responses[0]);
        DriveAsserts.AssertRejected(loser, OdinClientErrorCode.ZeroKnowledgeKeysAlreadySet);
        Assert.That(ZkBrowser.OpenMasterKey(Password, await GetStatus(owner)), Is.EqualTo(winner.MasterKey),
            "the stored keys are the ones the server said yes to");
    }

    [Test]
    public async Task APasswordChangeMustReLockTheKeys()
    {
        var owner = await LoginAsOwner();
        await CreateKeys(owner);

        var request = await OwnerPasswordFlow.BuildResetPasswordRequestAsync(Host, PrimaryIdentity, Password, NewPassword);
        var response = await owner.RefitFor<ITestSecurityContextOwnerClient>().ResetPassword(request);

        DriveAsserts.AssertRejected(response, OdinClientErrorCode.ZeroKnowledgeKeysRequired);
        var clientKey = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);
        var login = await OwnerPasswordFlow.LoginAsync(Host, PrimaryIdentity, Password, clientKey);
        Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the refused change must leave the old password working");
    }

    [Test]
    public async Task APasswordChangeKeepsTheSameMasterKey()
    {
        var owner = await LoginAsOwner();
        var keys = await CreateKeys(owner);

        var request = await OwnerPasswordFlow.BuildResetPasswordRequestAsync(Host, PrimaryIdentity, Password, NewPassword);
        request.ZkKeys = ZkBrowser.MakeKeys(NewPassword, keys.MasterKey).Request;
        var response = await owner.RefitFor<ITestSecurityContextOwnerClient>().ResetPassword(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);

        var status = await GetStatus(await OwnerSession.LoginAsync(Host, PrimaryIdentity, NewPassword));
        Assert.That(ZkBrowser.OpenMasterKey(NewPassword, status), Is.EqualTo(keys.MasterKey));
    }

    [Test]
    public async Task ConvertingNeedsTheKeysFirst()
    {
        var owner = await LoginAsOwner();
        var driveKeys = ZkBrowser.MakeDriveKeys(WellKnownAppDrives.ShardRecoveryDrive.Alias, ByteArrayUtil.GetRndByteArray(16));

        var response = await Client(owner).ConvertShardRecoveryDrive(driveKeys.Request);

        DriveAsserts.AssertRejected(response, OdinClientErrorCode.ZeroKnowledgeKeysNotSet);
    }

    [Test]
    public async Task TheConvertedDriveOpensOnlyThroughTheMasterKey()
    {
        var owner = await LoginAsOwner();
        var keys = await CreateKeys(owner);
        Assert.That((await GetShardRecoveryDrive(owner)).SharingModel, Is.EqualTo(DriveSharingModel.Collaborative),
            "a drive is labelled Private only once it is converted");
        var driveKeys = await ZkOwnerSteps.ConvertShardRecoveryDriveAsync(owner, keys.MasterKey);

        // A fresh login builds the owner's permissions from every drive; the converted one has no
        // master-key copy, and that must not break it.
        var freshOwner = await LoginAsOwner();
        var drive = await GetShardRecoveryDrive(freshOwner);

        Assert.That(drive.SharingModel, Is.EqualTo(DriveSharingModel.Private));
        Assert.That(drive.WriteOnlyPublicKeyJwk, Is.EqualTo(driveKeys.WriteOnlyPublicKeyJwk));

        // The browser's path: master key -> drive key (its check before skipping the conversion) ->
        // write-only private key.
        var driveKey = ZkBrowser.Open(drive.ZkMasterKeyEncryptedStorageKey, keys.MasterKey);
        Assert.That(driveKey, Is.EqualTo(driveKeys.DriveKey));
        Assert.That(ZkBrowser.Open(drive.ZkStorageKeyEncryptedWriteOnlyPrivateKey, driveKey),
            Is.EqualTo(driveKeys.WriteOnlyPrivateKey));
    }

    [Test]
    public async Task TheDriveCanBeConvertedAgain()
    {
        var owner = await LoginAsOwner();
        var keys = await CreateKeys(owner);
        await ZkOwnerSteps.ConvertShardRecoveryDriveAsync(owner, keys.MasterKey);
        var second = await ZkOwnerSteps.ConvertShardRecoveryDriveAsync(owner, keys.MasterKey);

        var drive = await GetShardRecoveryDrive(owner);
        Assert.That(ZkBrowser.Open(drive.ZkMasterKeyEncryptedStorageKey, keys.MasterKey), Is.EqualTo(second.DriveKey));
    }

    [Test]
    public async Task ConvertingIsRefusedWhenAFileUsesTheDriveKey()
    {
        var owner = await LoginAsOwner();
        var keys = await CreateKeys(owner);

        var upload = await UploadEncryptedFile(owner);
        Assert.That(upload.IsSuccessStatusCode, Is.True, $"actual {upload.StatusCode}: {upload.Error?.Content}");

        var driveKeys = ZkBrowser.MakeDriveKeys(WellKnownAppDrives.ShardRecoveryDrive.Alias, keys.MasterKey);
        var response = await Client(owner).ConvertShardRecoveryDrive(driveKeys.Request);

        DriveAsserts.AssertRejected(response, OdinClientErrorCode.DriveHasEncryptedFiles);
        Assert.That((await GetShardRecoveryDrive(owner)).ZkMasterKeyEncryptedStorageKey, Is.Null);
    }

    [Test]
    public async Task TheServerRefusesToEncryptWithAConvertedDrive()
    {
        var owner = await LoginAsOwner();
        var keys = await CreateKeys(owner);
        await ZkOwnerSteps.ConvertShardRecoveryDriveAsync(owner, keys.MasterKey);

        var freshOwner = await LoginAsOwner();

        // The upload client throws on any 400 rather than returning it.
        var e = Assert.ThrowsAsync<System.Exception>(() => UploadEncryptedFile(freshOwner));
        Assert.That(e!.Message, Does.EndWith(nameof(OdinClientErrorCode.DriveIsZeroKnowledge)));
    }

    // ---------------------------------------------------------------------------------------------

    private static IZeroKnowledgeOwnerClient Client(OwnerSession owner) => ZkOwnerSteps.Client(owner);

    private static Task<ZkStatusResponse> GetStatus(OwnerSession owner) => ZkOwnerSteps.GetStatusAsync(owner);

    private static Task<ZkBrowser.Keys> CreateKeys(OwnerSession owner) => ZkOwnerSteps.CreateKeysAsync(owner, Password);

    private static Task<OwnerClientDriveData> GetShardRecoveryDrive(OwnerSession owner) => ZkOwnerSteps.GetShardRecoveryDriveAsync(owner);

    private static async Task<IApiResponse> UploadEncryptedFile(OwnerSession owner)
    {
        var metadata = SampleMetadataData.CreateWithContent(fileType: 100, "encrypted content", AccessControlList.OwnerOnly);
        var (upload, _, _, _) = await owner.Drives.Writer.CreateEncryptedFile(
            WellKnownAppDrives.ShardRecoveryDrive.Alias, metadata, transitOptions: null!);
        return upload;
    }
}
