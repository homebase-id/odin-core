using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.Mail;

/// <summary>
/// The setup flow with tenant mail turned ON for this fixture only — the per-fixture config hook,
/// not process-wide environment variables, so the fixtures running beside this one still see the
/// flag off.
///
/// Email:Provider stays None, so NullMailboxProvider serves: these tests are about the flow's
/// shape and its idempotence, which is what has to hold before a real mail server exists.
/// </summary>
public class V2MailFlowTests : V2Fixture
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
            // Without a storage key the DKIM store is unconfigured and the DKIM steps no-op.
            ["Email:DkimStorageKey"] = "BAADF00DBAADF00DBAADF00DBAADF00DBAADF00DBAADF00DBAADF00DBAADF00D",
        };

    private static DriveSpec EmailDrive() =>
        new(WellKnownAppDrives.EmailAppDrive, "Email", AllowAnonymousReads: false, OwnerOnly: true);

    private async Task<V2MailClient> EmailAppAsync()
    {
        var caller = await SetupCaller(CallerSpec.App(EmailDrive(), DrivePermission.ReadWrite));
        return new V2MailClient(caller.Identity, caller.Factory);
    }

    [Test]
    public async Task MailboxSetupReportsBackThroughStatus()
    {
        var mail = await EmailAppAsync();

        var setup = await mail.EnsureMailboxAsync(Address);
        Assert.That(setup.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(setup.Content!.PrimaryEmailAddress, Is.EqualTo(Address));
        Assert.That(setup.Content.DkimRecords, Is.Not.Empty, "activation generates DKIM records");

        var status = await mail.GetStatusAsync();
        Assert.That(status.Content!.TenantMailEnabled, Is.True);
        Assert.That(status.Content.MailboxProvisioned, Is.True);
        Assert.That(status.Content.PrimaryEmailAddress, Is.EqualTo(Address));

        // The key is the last step and has not run, so this identity is not activated yet.
        Assert.That(status.Content.Activated, Is.False);
    }

    /// <summary>
    /// The property the whole resumable-setup design rests on: a client that was killed mid-step
    /// re-runs it rather than tracking where it got to. Re-running must not churn the DKIM keys —
    /// rotation is a deliberate act, and new keys here would silently break DNS that was already
    /// published.
    /// </summary>
    [Test]
    public async Task MailboxSetupIsIdempotent()
    {
        var mail = await EmailAppAsync();

        var first = await mail.EnsureMailboxAsync(Address);
        var second = await mail.EnsureMailboxAsync(Address);

        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(
            second.Content!.DkimRecords.ConvertAll(r => r.Value),
            Is.EqualTo(first.Content!.DkimRecords.ConvertAll(r => r.Value)),
            "a re-run must keep the existing DKIM keys");
    }

    [Test]
    public async Task MailboxRejectsAnAddressAtAnotherDomain()
    {
        var mail = await EmailAppAsync();

        var response = await mail.EnsureMailboxAsync("someone@example.com");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    /// <summary>
    /// App passwords need a published key, and the key is the last setup step — so asking for one
    /// too early is refused rather than issuing a credential for a mailbox nothing can encrypt to.
    /// </summary>
    [Test]
    public async Task AppPasswordBeforeActivationIsRefused()
    {
        var mail = await EmailAppAsync();
        await mail.EnsureMailboxAsync(Address);

        var response = await mail.IssueAppPasswordAsync(Address, "Thunderbird");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    /// <summary>
    /// Revoke is idempotent by contract: the client reconciles its own drive records against the
    /// mail server, and an id that is already gone must not fail that reconciliation.
    /// </summary>
    [Test]
    public async Task RevokingAnUnknownAppPasswordSucceeds()
    {
        var mail = await EmailAppAsync();

        var response = await mail.RevokeAppPasswordAsync("not-a-real-credential-id");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    /// <summary>
    /// No mail server here means no usage figures. The endpoint says so instead of failing, so
    /// the status screen simply omits the storage line.
    /// </summary>
    [Test]
    public async Task StorageDegradesWhenTheProviderCannotAnswer()
    {
        var mail = await EmailAppAsync();

        var response = await mail.GetStorageAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content!.Available, Is.False);
    }

    /// <summary>The gate still applies with the flag on — a half grant is not the email app.</summary>
    [Test]
    public async Task SetupStillRequiresReadAndWrite()
    {
        var caller = await SetupCaller(CallerSpec.App(EmailDrive(), DrivePermission.Read));
        var mail = new V2MailClient(caller.Identity, caller.Factory);

        var response = await mail.EnsureMailboxAsync(Address);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }
}
