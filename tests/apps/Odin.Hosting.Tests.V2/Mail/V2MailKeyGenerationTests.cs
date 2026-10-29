#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Drives;
using Odin.Services.Email;

namespace Odin.Hosting.Tests.V2.Mail;

/// <summary>
/// Key generation, and the ordering invariant the whole custody argument rests on: the keyring is
/// on the drive BEFORE its certificate is published. If those ever swap, an app killed at the
/// wrong moment leaves an identity with mail encrypted to a key nobody holds.
/// </summary>
public class V2MailKeyGenerationTests : V2Fixture
{
    private const string Address = "mail@frodo.dotyou.cloud";

    protected override IReadOnlyDictionary<string, string?> ConfigOverrides =>
        new Dictionary<string, string?>
        {
            ["Email:TenantMail:Enabled"] = "true",
            ["Email:TenantMail:MxNodes:0"] = "mx1.dotyou.cloud",
            ["Email:TenantMail:SpfIncludeTarget"] = "spf.dotyou.cloud",
            ["Email:TenantMail:DmarcReportEmail"] = "dmarc@dotyou.cloud",
            ["Email:TenantMail:TlsReportEmail"] = "tlsrpt@dotyou.cloud",
            ["Email:DkimStorageKey"] = "BAADF00DBAADF00DBAADF00DBAADF00DBAADF00DBAADF00DBAADF00DBAADF00D",
        };

    private static DriveSpec EmailDrive() =>
        new(WellKnownAppDrives.EmailAppDrive, "Email", AllowAnonymousReads: false, OwnerOnly: true);

    private async Task<(V2MailClient Mail, IV2Caller Caller)> SetUpMailboxAsync()
    {
        var caller = await SetupCaller(CallerSpec.App(EmailDrive(), DrivePermission.ReadWrite));
        var mail = new V2MailClient(caller.Identity, caller.Factory);
        await mail.EnsureMailboxAsync(Address);
        return (mail, caller);
    }

    [Test]
    public async Task GeneratingAKeyActivatesTheIdentity()
    {
        var (mail, _) = await SetUpMailboxAsync();

        var generated = await mail.GenerateKeyAsync(Address);

        Assert.That(generated.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(generated.Content!.KeyFileUniqueId, Is.Not.EqualTo(Guid.Empty));
        Assert.That(generated.Content.FingerprintHex, Is.Not.Empty);

        var status = await mail.GetStatusAsync();
        Assert.That(status.Content!.Activated, Is.True);
        Assert.That(status.Content.PublicKeyFingerprint, Is.EqualTo(generated.Content.FingerprintHex));
        Assert.That(status.Content.CurrentKeyFileUniqueId, Is.EqualTo(generated.Content.KeyFileUniqueId));
    }

    /// <summary>
    /// The keyring — both halves — is a real, readable file on the email drive, written by the
    /// server. This is what replaces "returned exactly once", and it is why an app killed mid-setup
    /// loses nothing.
    /// </summary>
    [Test]
    public async Task TheKeyringIsOnTheDriveWithItsPrivateHalf()
    {
        var (mail, caller) = await SetUpMailboxAsync();
        var generated = await mail.GenerateKeyAsync(Address);
        var uniqueId = generated.Content!.KeyFileUniqueId;

        var reader = new DriveReaderV2Client(caller.Identity, caller.Factory);
        var header = await reader.GetFileHeaderByUniqueIdAsync(uniqueId, WellKnownAppDrives.EmailAppDrive.Alias);

        Assert.That(header.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(header.Content!.FileMetadata.AppData.FileType, Is.EqualTo(EmailDriveFileTypes.KeyMaterial));
        Assert.That(header.Content.FileMetadata.IsEncrypted, Is.True, "the keyring is encrypted at rest");

        var content = DecryptHeaderContent(header.Content, reader);
        Assert.That(content, Does.Contain("-----BEGIN PGP PRIVATE KEY BLOCK-----"),
            "the private half must be on the drive - it exists nowhere else");
        Assert.That(content, Does.Contain(generated.Content.FingerprintHex));
    }

    /// <summary>
    /// On a private Email drive the server holds no key to lock the keyring with: it seals the file key to the
    /// drive's write-only public key, as a deposit is, so only the owner's devices open it (plan 4.8).
    /// </summary>
    [Test]
    public async Task OnAPrivateEmailDriveTheKeyringIsSealedToTheDrive()
    {
        var owner = await LoginAsOwner(PrimaryIdentity);
        var (zkMasterKey, _, _) = await Odin.Hosting.Tests.V2.ZeroKnowledge.ZkOwnerSteps.MakeZeroKnowledgeAsync(owner,
            Odin.Hosting.Tests.V2.Auth.OwnerLogin.DefaultPassword);
        var keys = Odin.Hosting.Tests.V2.ZeroKnowledge.ZkBrowser.MakeDriveKeys(WellKnownAppDrives.EmailAppDrive.Alias, zkMasterKey);
        await owner.Admin.CreateDrive(WellKnownAppDrives.EmailAppDrive, "Email", allowAnonymousReads: false, ownerOnly: true,
            appId: Odin.Services.Apps.SystemAppConstants.EmailAppId, driveSlug: new OdinSlug("email"),
            driveTypeSlug: new OdinSlug("email"), sharingModel: DriveSharingModel.Private, zkKeys: keys.Request);

        var (mail, _) = await SetUpMailboxAsync();
        var generated = await mail.GenerateKeyAsync(Address);
        Assert.That(generated.StatusCode, Is.EqualTo(HttpStatusCode.OK), generated.Error?.Content);

        var header = await owner.Drives.Reader.GetFileHeaderByUniqueIdAsync(generated.Content!.KeyFileUniqueId,
            WellKnownAppDrives.EmailAppDrive.Alias);
        Assert.That(header.StatusCode, Is.EqualTo(HttpStatusCode.OK), header.Error?.Content);
        var content = Odin.Hosting.Tests.V2.ZeroKnowledge.ZkBrowser.OpenDeposit(header.Content!, keys.WriteOnlyPrivateKey)
            .ToStringFromUtf8Bytes();
        Assert.That(content, Does.Contain("-----BEGIN PGP PRIVATE KEY BLOCK-----"));

        // The pointer too.  It moves on rotation, by a caller that holds the drive's key (here the owner): one that
        // doesn't can't overwrite a sealed file, as it can't read one, and signs in again first
        var ownerMail = new V2MailClient(owner.Identity, owner.Factory);
        var second = await ownerMail.GenerateKeyAsync(Address);
        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.OK), second.Error?.Content);
        var pointer = await owner.Drives.Reader.GetFileHeaderByUniqueIdAsync(EmailDriveFileTypes.CurrentKeyPointerUniqueId,
            WellKnownAppDrives.EmailAppDrive.Alias);
        Assert.That(pointer.StatusCode, Is.EqualTo(HttpStatusCode.OK), pointer.Error?.Content);
        Assert.That(Odin.Hosting.Tests.V2.ZeroKnowledge.ZkBrowser.OpenDeposit(pointer.Content!, keys.WriteOnlyPrivateKey)
            .ToStringFromUtf8Bytes(), Does.Contain(second.Content!.KeyFileUniqueId.ToString()));
    }

    /// <summary>
    /// Rotation appends. The old keyring stays exactly where it was, because mail received under
    /// it is only readable while it survives; only the pointer moves.
    /// </summary>
    [Test]
    public async Task RotationAppendsAndMovesThePointer()
    {
        var (mail, caller) = await SetUpMailboxAsync();

        var first = await mail.GenerateKeyAsync(Address);
        var second = await mail.GenerateKeyAsync(Address);
        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.OK), second.Error?.Content);

        Assert.That(second.Content!.KeyFileUniqueId, Is.Not.EqualTo(first.Content!.KeyFileUniqueId));
        Assert.That(second.Content.FingerprintHex, Is.Not.EqualTo(first.Content.FingerprintHex));

        var reader = new DriveReaderV2Client(caller.Identity, caller.Factory);

        // The old keyring is still there.
        var old = await reader.GetFileHeaderByUniqueIdAsync(
            first.Content.KeyFileUniqueId, WellKnownAppDrives.EmailAppDrive.Alias);
        Assert.That(old.StatusCode, Is.EqualTo(HttpStatusCode.OK), "an older keyring is never deleted");

        // And the identity now publishes the new one.
        var status = await mail.GetStatusAsync();
        Assert.That(status.Content!.PublicKeyFingerprint, Is.EqualTo(second.Content.FingerprintHex));
        Assert.That(status.Content.CurrentKeyFileUniqueId, Is.EqualTo(second.Content.KeyFileUniqueId));
    }

    /// <summary>Entropy is optional — desktop and web have no sensor, and must still get a key.</summary>
    [Test]
    public async Task GenerationWithoutClientEntropySucceeds()
    {
        var (mail, _) = await SetUpMailboxAsync();

        var generated = await mail.GenerateKeyAsync(Address, clientEntropyBase64: "");

        Assert.That(generated.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(generated.Content!.ClientEntropyUsed, Is.False, "reported honestly, not assumed");
    }

    [Test]
    public async Task GenerationWithClientEntropyReportsThatItWasUsed()
    {
        var (mail, _) = await SetUpMailboxAsync();
        var entropy = Convert.ToBase64String(ByteArrayUtil.GetRndByteArray(64));

        var generated = await mail.GenerateKeyAsync(Address, entropy);

        Assert.That(generated.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(generated.Content!.ClientEntropyUsed, Is.True);
    }

    /// <summary>
    /// Too little entropy is refused rather than quietly accepted: a caller sending 4 bytes and
    /// believing it seeded its key would be worse off than one sending none.
    /// </summary>
    [Test]
    public async Task GenerationWithTooLittleEntropyIsRejected()
    {
        var (mail, _) = await SetUpMailboxAsync();
        var tooShort = Convert.ToBase64String(new byte[8]);

        var generated = await mail.GenerateKeyAsync(Address, tooShort);

        Assert.That(generated.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    /// <summary>An app password can only be issued once a key exists — the ordering step 5 exposed.</summary>
    [Test]
    public async Task AppPasswordWorksOnceAKeyExists()
    {
        var (mail, _) = await SetUpMailboxAsync();
        await mail.GenerateKeyAsync(Address);

        var issued = await mail.IssueAppPasswordAsync(Address, "Thunderbird");

        Assert.That(issued.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(issued.Content!.Secret, Is.Not.Empty);
        Assert.That(issued.Content.Id, Is.Not.Empty, "the id is the only handle a revoke has");

        var revoked = await mail.RevokeAppPasswordAsync(issued.Content.Id);
        Assert.That(revoked.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    private static string DecryptHeaderContent(
        Odin.Services.Apps.SharedSecretEncryptedFileHeader header,
        DriveReaderV2Client reader)
    {
        var sharedSecret = reader.GetSharedSecret();
        var keyHeader = header.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref sharedSecret);
        var cipher = Convert.FromBase64String(header.FileMetadata.AppData.Content);
        return keyHeader.Decrypt(cipher).ToStringFromUtf8Bytes();
    }
}
