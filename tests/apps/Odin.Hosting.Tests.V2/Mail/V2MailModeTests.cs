#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Drives;
using Odin.Services.Email;
using Odin.Services.Fingering;

namespace Odin.Hosting.Tests.V2.Mail;

/// <summary>
/// Encrypted and standard mailboxes: chosen at setup, switchable later. A standard mailbox has no
/// key to wait for, and switching to it unpublishes the email certificate and nothing else.
/// </summary>
public class V2MailModeTests : V2Fixture
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

    private async Task<(V2MailClient Mail, IV2Caller Caller)> EmailAppAsync()
    {
        var caller = await SetupCaller(CallerSpec.App(EmailDrive(), DrivePermission.ReadWrite));
        return (new V2MailClient(caller.Identity, caller.Factory), caller);
    }

    private async Task<HttpResponseMessage> GetAnonymousAsync(IV2Caller caller, string path)
    {
        using var anonymous = Host.CreateAnonymousClient(caller.Identity.DomainName);
        return await anonymous.GetAsync(path);
    }

    private async Task<HttpStatusCode> GetAnonymousStatusAsync(IV2Caller caller, string path) =>
        (await GetAnonymousAsync(caller, path)).StatusCode;

    private async Task<DidWebResponse> GetDidAsync(IV2Caller caller)
    {
        var response = await GetAnonymousAsync(caller, ".well-known/did.json");
        return OdinSystemSerializer.Deserialize<DidWebResponse>(await response.Content.ReadAsStringAsync())!;
    }

    [Test]
    public async Task AStandardMailboxIsReadyWithoutAKey()
    {
        var (mail, caller) = await EmailAppAsync();

        var setup = await mail.EnsureMailboxAsync(Address, MailboxMode.Standard);
        Assert.That(setup.StatusCode, Is.EqualTo(HttpStatusCode.OK), setup.Error?.Content);

        var status = (await mail.GetStatusAsync()).Content!;
        Assert.That(status.Mode, Is.EqualTo(MailboxMode.Standard));
        Assert.That(status.Activated, Is.True);
        Assert.That(status.CurrentKeyFileUniqueId, Is.Null);
        Assert.That(status.PublicKeyFingerprint, Is.Null);

        var issued = await mail.IssueAppPasswordAsync(Address, "Apple Mail");
        Assert.That(issued.StatusCode, Is.EqualTo(HttpStatusCode.OK), issued.Error?.Content);

        Assert.That(await GetAnonymousStatusAsync(caller, ".well-known/autoconfig/mail/config-v1.1.xml"),
            Is.EqualTo(HttpStatusCode.OK), "autoconfig is how a standard mail app finds the servers");
        Assert.That(await GetAnonymousStatusAsync(caller, ".well-known/openpgpkey/hu/anyhash"),
            Is.EqualTo(HttpStatusCode.NotFound));
    }

    /// <summary>Clients that predate the choice only ever set up encrypted mailboxes.</summary>
    [Test]
    public async Task NoModeMeansEncrypted()
    {
        var (mail, _) = await EmailAppAsync();
        await mail.EnsureMailboxAsync(Address);

        var status = (await mail.GetStatusAsync()).Content!;

        Assert.That(status.Mode, Is.EqualTo(MailboxMode.Encrypted));
        Assert.That(status.Activated, Is.False, "an encrypted mailbox waits for its key");
    }

    [Test]
    public async Task ReRunningTheMailboxStepKeepsTheChosenMode()
    {
        var (mail, _) = await EmailAppAsync();
        await mail.EnsureMailboxAsync(Address, MailboxMode.Standard);

        await mail.EnsureMailboxAsync(Address, MailboxMode.Encrypted);

        Assert.That((await mail.GetStatusAsync()).Content!.Mode, Is.EqualTo(MailboxMode.Standard));
    }

    /// <summary>A key appearing on a standard mailbox would turn encryption on behind the mode's back.</summary>
    [Test]
    public async Task GeneratingAKeyIsRefusedOnAStandardMailbox()
    {
        var (mail, _) = await EmailAppAsync();
        await mail.EnsureMailboxAsync(Address, MailboxMode.Standard);

        var generated = await mail.GenerateKeyAsync(Address);

        Assert.That(generated.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task SwitchingBeforeTheMailboxExistsIsRefused()
    {
        var (mail, _) = await EmailAppAsync();

        var response = await mail.SetModeAsync(MailboxMode.Standard);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    /// <summary>
    /// Only the email certificate goes: the DID document keeps its authentication key and
    /// everything else, and the keyring stays on the drive so mail already stored can still be read.
    /// </summary>
    [Test]
    public async Task SwitchingToStandardUnpublishesOnlyTheEmailKey()
    {
        var (mail, caller) = await EmailAppAsync();
        await mail.EnsureMailboxAsync(Address);
        var key = (await mail.GenerateKeyAsync(Address)).Content!;
        var didBefore = await GetDidAsync(caller);
        Assert.That(didBefore.KeyAgreement, Is.Not.Null.And.Not.Empty);

        var switched = await mail.SetModeAsync(MailboxMode.Standard);
        Assert.That(switched.StatusCode, Is.EqualTo(HttpStatusCode.OK), switched.Error?.Content);

        var status = switched.Content!;
        Assert.That(status.Mode, Is.EqualTo(MailboxMode.Standard));
        Assert.That(status.Activated, Is.True);
        Assert.That(status.PublicKeyFingerprint, Is.Null);
        Assert.That(status.CurrentKeyFileUniqueId, Is.EqualTo(key.KeyFileUniqueId));

        Assert.That(await GetAnonymousStatusAsync(caller, ".well-known/openpgpkey/hu/anyhash"),
            Is.EqualTo(HttpStatusCode.NotFound));

        var didAfter = await GetDidAsync(caller);
        Assert.That(didAfter.KeyAgreement, Is.Null.Or.Empty);
        Assert.That(didAfter.Authentication, Is.EqualTo(didBefore.Authentication));
        Assert.That(
            didAfter.VerificationMethod!.Select(v => v.Id),
            Is.EqualTo(didBefore.VerificationMethod!.Select(v => v.Id).Where(id => !id!.EndsWith("#key-agreement"))));
        Assert.That(didAfter.Service.Select(s => s.Id), Is.EqualTo(didBefore.Service.Select(s => s.Id)));

        var reader = new DriveReaderV2Client(caller.Identity, caller.Factory);
        var keyring = await reader.GetFileHeaderByUniqueIdAsync(key.KeyFileUniqueId, WellKnownAppDrives.EmailAppDrive.Alias);
        Assert.That(keyring.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the keyring outlives the switch");

        var issued = await mail.IssueAppPasswordAsync(Address, "Apple Mail");
        Assert.That(issued.StatusCode, Is.EqualTo(HttpStatusCode.OK), issued.Error?.Content);
    }

    [Test]
    public async Task SwitchingIsIdempotent()
    {
        var (mail, _) = await EmailAppAsync();
        await mail.EnsureMailboxAsync(Address);
        await mail.GenerateKeyAsync(Address);

        await mail.SetModeAsync(MailboxMode.Standard);
        var again = await mail.SetModeAsync(MailboxMode.Standard);

        Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.OK), again.Error?.Content);
        Assert.That(again.Content!.Mode, Is.EqualTo(MailboxMode.Standard));
    }

    [Test]
    public async Task SwitchingBackToEncryptedPublishesANewKey()
    {
        var (mail, caller) = await EmailAppAsync();
        await mail.EnsureMailboxAsync(Address);
        var first = (await mail.GenerateKeyAsync(Address)).Content!;
        await mail.SetModeAsync(MailboxMode.Standard);

        var switched = await mail.SetModeAsync(MailboxMode.Encrypted);
        Assert.That(switched.StatusCode, Is.EqualTo(HttpStatusCode.OK), switched.Error?.Content);

        var status = switched.Content!;
        Assert.That(status.Mode, Is.EqualTo(MailboxMode.Encrypted));
        Assert.That(status.Activated, Is.True);
        Assert.That(status.PublicKeyFingerprint, Is.Not.Null.And.Not.EqualTo(first.FingerprintHex));
        Assert.That(status.CurrentKeyFileUniqueId, Is.Not.EqualTo(first.KeyFileUniqueId));

        Assert.That(await GetAnonymousStatusAsync(caller, ".well-known/openpgpkey/hu/anyhash"),
            Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await GetDidAsync(caller)).KeyAgreement, Is.Not.Null.And.Not.Empty);
    }

    /// <summary>Standard from the start has never had a key; switching generates its first.</summary>
    [Test]
    public async Task AStandardMailboxCanBecomeEncrypted()
    {
        var (mail, _) = await EmailAppAsync();
        await mail.EnsureMailboxAsync(Address, MailboxMode.Standard);

        var switched = await mail.SetModeAsync(MailboxMode.Encrypted);

        Assert.That(switched.StatusCode, Is.EqualTo(HttpStatusCode.OK), switched.Error?.Content);
        Assert.That(switched.Content!.Mode, Is.EqualTo(MailboxMode.Encrypted));
        Assert.That(switched.Content.CurrentKeyFileUniqueId, Is.Not.Null);
        Assert.That(switched.Content.PublicKeyFingerprint, Is.Not.Null);
    }
}
