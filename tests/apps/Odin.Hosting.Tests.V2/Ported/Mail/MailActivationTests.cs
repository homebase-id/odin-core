#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Cryptography.Pgp;
using Odin.Core.Serialization;
using Odin.Hosting.Controllers.OwnerToken.Mail;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Mail;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Email;
using Odin.Services.Fingering;
using Refit;

namespace Odin.Hosting.Tests.V2.Ported.Mail;

/// <summary>
/// Port of <c>OwnerApi/Mail/MailActivationTests</c>. The activation flow
/// (<c>docs/email-keys-plan.md</c>) end to end with tenant mail ENABLED (config overrides below; the
/// production default stays off — flag-off behavior is asserted separately in
/// <see cref="MailActivationFlagOffTests"/>).
/// </summary>
/// <remarks>
/// <para>
/// The original turned the flag on with <c>RunBeforeAnyTests(envOverrides:)</c> and undid it in a
/// <c>[TearDown]</c>, with a comment explaining that env vars are process-wide and would otherwise
/// leak into every later fixture. Here that is <see cref="V2Fixture.ConfigOverrides"/>, which is
/// merged into this fixture's host config only — so the leak the original had to clean up cannot
/// happen, and the tear-down goes away with it. List settings bind by index
/// (<c>Email:TenantMail:MxNodes:0</c>), not the <c>__0</c> form. The sibling fixtures in
/// <c>V2/Mail/</c> use the same shape.
/// </para>
/// <para>
/// No log-event override: tenant mail on is no longer a config error. It used to be, while tenant
/// mail and the system sender were coupled, and this fixture had to tolerate exactly one deliberate
/// ERR. With Mailgun moved back out of the Email section there is nothing to tolerate, so the
/// default "zero errors" assertion applies — which is a stronger check than the one it replaces.
/// Unlike the V1 original, that assertion is live here (see <see cref="V2Fixture.AssertNoErrorLogEvents"/>).
/// </para>
/// <para>
/// The mail endpoints are the system under test — several tests assert a refusal — so they go
/// through <see cref="OwnerSession.RefitFor{T}"/> on
/// <see cref="IMailTestHttpClientForOwner"/> rather than the V1 <c>MailApiClient</c> wrapper, which
/// needs <c>OwnerApiTestUtils</c>. The anonymous well-known surfaces are read with
/// <c>Host.CreateClient()</c> instead of <c>WebScaffold.HttpClientFactory</c>, and the port numbers
/// drop out of the URLs because there is no listener.
/// </para>
/// <para>No caller matrix in the original and none added.</para>
/// </remarks>
[TestFixture]
public class MailActivationTests : V2Fixture
{
    private static readonly string Domain = Identities.Frodo;
    private const string PrimaryAddress = "frodo@frodo.dotyou.cloud";

    protected override IReadOnlyDictionary<string, string?> ConfigOverrides =>
        new Dictionary<string, string?>
        {
            ["Email:TenantMail:Enabled"] = "true",
            ["Email:TenantMail:MxNodes:0"] = "mx1.dotyou.cloud",
            ["Email:TenantMail:MxNodes:1"] = "mx2.dotyou.cloud",
            ["Email:TenantMail:SpfIncludeTarget"] = "_spf.dotyou.cloud",
            ["Email:TenantMail:DmarcReportEmail"] = "dmarc-reports@dotyou.cloud",
            ["Email:TenantMail:TlsReportEmail"] = "tls-reports@dotyou.cloud",
            ["Email:DkimStorageKey"] = "BAADF00DBAADF00DBAADF00DBAADF00DBAADF00DBAADF00DBAADF00DBAADF00D",
        };

    private async Task<IMailTestHttpClientForOwner> MailClientAsync()
    {
        var owner = await LoginAsOwner();
        return owner.RefitFor<IMailTestHttpClientForOwner>();
    }

    private static Task<ApiResponse<MailActivationResult>> Activate(
        IMailTestHttpClientForOwner client, string publicCertificateArmored, string primaryEmailAddress) =>
        client.Activate(new ActivateMailRequest
        {
            PublicCertificateArmored = publicCertificateArmored,
            PrimaryEmailAddress = primaryEmailAddress,
        });

    [Test]
    public async Task ItShouldActivateIdempotentlyAndReportStatus()
    {
        var client = await MailClientAsync();

        // Before: enabled but not activated
        var statusBefore = await client.GetStatus();
        Assert.That(statusBefore.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(statusBefore.Content!.TenantMailEnabled, Is.True);
        Assert.That(statusBefore.Content.Activated, Is.False);
        Assert.That(statusBefore.Content.DkimRecords, Is.Empty);

        // Activate
        var material = OpenPgpKeyManagement.GenerateP384KeyMaterial(PrimaryAddress);
        var activation = await Activate(client, material.PublicCertificateArmored, PrimaryAddress);
        Assert.That(activation.StatusCode, Is.EqualTo(HttpStatusCode.OK), activation.Error?.Content);

        var result = activation.Content!;
        // No PowerDNS in the test environment -> the manual-instructions path
        Assert.That(result.DnsRecordsWritten, Is.False);
        Assert.That(result.DkimRecords.Select(r => r.Name), Is.EquivalentTo(new[] { "s1._domainkey", "s2._domainkey" }));
        Assert.That(result.DkimRecords.All(r => r.Type == "TXT" && r.Optional), Is.True);
        Assert.That(result.DkimRecords.Single(r => r.Name == "s1._domainkey").Value, Does.StartWith("v=DKIM1; k=ed25519; p="));
        Assert.That(result.DkimRecords.Single(r => r.Name == "s2._domainkey").Value, Does.StartWith("v=DKIM1; k=rsa; p="));

        // After: activated with the certificate's fingerprint
        var statusAfter = await client.GetStatus();
        Assert.That(statusAfter.Content!.Activated, Is.True);
        Assert.That(statusAfter.Content.PublicKeyFingerprint, Is.EqualTo(material.FingerprintHex));
        Assert.That(statusAfter.Content.DkimRecords.Count, Is.EqualTo(2));

        // Re-activation is idempotent: the DKIM pair is kept, not regenerated
        var secondActivation = await Activate(client, material.PublicCertificateArmored, PrimaryAddress);
        Assert.That(secondActivation.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(secondActivation.Content!.DkimRecords.Select(r => r.Value),
            Is.EquivalentTo(result.DkimRecords.Select(r => r.Value)));

        // The anonymous surfaces went live: WKD serves, DID gains keyAgreement,
        // autoconfig answers (flag is on here)
        using var anonymousClient = Host.CreateClient();

        var wkd = await anonymousClient.GetAsync($"https://{Domain}/.well-known/openpgpkey/hu/anyhash");
        Assert.That(wkd.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var did = await anonymousClient.GetAsync($"https://{Domain}/.well-known/did.json");
        var didDoc = OdinSystemSerializer.Deserialize<DidWebResponse>(await did.Content.ReadAsStringAsync());
        Assert.That(didDoc!.KeyAgreement, Is.Not.Null.And.Not.Empty);

        var autoconfig = await anonymousClient.GetAsync($"https://{Domain}/.well-known/autoconfig/mail/config-v1.1.xml");
        Assert.That(autoconfig.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var xml = await autoconfig.Content.ReadAsStringAsync();
        Assert.That(xml, Does.Contain("<hostname>mx1.dotyou.cloud</hostname>"));
        Assert.That(xml, Does.Contain("<hostname>mx2.dotyou.cloud</hostname>"));
    }

    [Test]
    public async Task ItShouldProvisionAnAppPasswordOnceActivated()
    {
        var client = await MailClientAsync();

        // Not activated yet -> refused
        var refused = await client.ProvisionAppPassword(new AppPasswordRequest
        {
            PrimaryEmailAddress = PrimaryAddress,
            Label = "Thunderbird",
        });
        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var material = OpenPgpKeyManagement.GenerateP384KeyMaterial(PrimaryAddress);
        var activation = await Activate(client, material.PublicCertificateArmored, PrimaryAddress);
        Assert.That(activation.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var response = await client.ProvisionAppPassword(new AppPasswordRequest
        {
            PrimaryEmailAddress = PrimaryAddress,
            Label = "Thunderbird",
        });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content!.Password, Does.Match(new Regex("^[a-z2-7]{5}(-[a-z2-7]{5}){3}$")));

        // Each provisioning yields a fresh password
        var second = await client.ProvisionAppPassword(new AppPasswordRequest
        {
            PrimaryEmailAddress = PrimaryAddress,
            Label = "FairEmail",
        });
        Assert.That(second.Content!.Password, Is.Not.EqualTo(response.Content.Password));
    }

    [Test]
    public async Task ItShouldRoundTripTheChallengeAndAnswerVerify()
    {
        var client = await MailClientAsync();

        // Not activated -> challenge refused
        var refused = await client.CreateChallenge();
        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var material = OpenPgpKeyManagement.GenerateP384KeyMaterial(PrimaryAddress);
        var activation = await Activate(client, material.PublicCertificateArmored, PrimaryAddress);
        Assert.That(activation.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // The full owner round-trip check, exactly as the app performs it: decrypt the
        // challenge with the private keyring and compare hashes
        var challenge = await client.CreateChallenge();
        Assert.That(challenge.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var nonce = OpenPgpKeyManagement.Decrypt(
            Convert.FromBase64String(challenge.Content!.EncryptedNonceBase64), material.SecretKeyArmored);
        var nonceHash = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(nonce));
        Assert.That(nonceHash, Is.EqualTo(challenge.Content.NonceSha256Base64));

        // A fresh challenge is a fresh nonce
        var second = await client.CreateChallenge();
        Assert.That(second.Content!.EncryptedNonceBase64, Is.Not.EqualTo(challenge.Content.EncryptedNonceBase64));

        // The verify endpoint answers with findings for the activated tenant; the
        // finding logic itself is unit-tested (EmailHealthVerifierTest) - live DNS
        // and surface reachability vary by environment, so only the shape is asserted
        var verify = await client.Verify();
        Assert.That(verify.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(verify.Content!.Activated, Is.True);
    }

    [Test]
    public async Task ItShouldRejectBadInput()
    {
        var client = await MailClientAsync();
        var material = OpenPgpKeyManagement.GenerateP384KeyMaterial(PrimaryAddress);

        // Foreign address
        var foreignAddress = await Activate(client, material.PublicCertificateArmored, "frodo@sam.dotyou.cloud");
        Assert.That(foreignAddress.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        // Garbage certificate
        var garbage = await Activate(client, "not a certificate", PrimaryAddress);
        Assert.That(garbage.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        // A secret keyring must be refused before anything is stored
        var secret = await Activate(client, material.SecretKeyArmored, PrimaryAddress);
        Assert.That(secret.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var status = await client.GetStatus();
        Assert.That(status.Content!.Activated, Is.False, "nothing may be activated after refusals");
    }

    /// <summary>
    /// The owner console's "publish my mail DNS" button, on the path it will most often take
    /// here and on any self-hosted identity: PowerDNS is not configured, so the records cannot
    /// be written and must come back as instructions instead.
    ///
    /// That false-with-records branch is what drives the manual-records UI, and it is the one
    /// that can regress silently - a caller that only checks the HTTP status would see success
    /// either way.
    /// </summary>
    [Test]
    public async Task ItShouldReturnMailRecordsAsInstructionsWhenDnsIsNotOursToWrite()
    {
        var client = await MailClientAsync();

        var response = await client.PublishDnsRecords();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = response.Content!;
        Assert.That(result.DnsRecordsWritten, Is.False, "the test host configures no PowerDNS");
        Assert.That(result.Records, Is.Not.Empty, "records must still be returned, as instructions");

        // The set is the configured mail infrastructure, derived from ConfigOverrides above.
        var types = result.Records.Select(x => x.Type).Distinct().ToList();
        Assert.That(types, Does.Contain("MX"));
        Assert.That(types, Does.Contain("TXT"));

        var mx = result.Records.Where(x => x.Type == "MX").Select(x => x.Value).ToList();
        Assert.That(mx.Any(v => v.Contains("mx1.dotyou.cloud")), Is.True, "MxNodes[0] must be published");
        Assert.That(mx.Any(v => v.Contains("mx2.dotyou.cloud")), Is.True, "MxNodes[1] must be published");

        // Every record published here must be Optional-flagged. That flag is the ONLY thing
        // separating the mail set from the identity's required records, and publishing a
        // required record from this button would put the certificate/validation gate at risk.
        Assert.That(result.Records.All(x => x.Optional), Is.True, "only optional (mail) records");

        // www is optional-in-spirit but is NOT Optional-flagged - it is probed separately by
        // DnsHealthService. If it ever gains the flag it would silently join this write.
        Assert.That(result.Records.Any(x => x.Name == "www"), Is.False, "www is not a mail record");
    }
}

/// <summary>
/// Port of <c>OwnerApi/Mail/MailActivationTests</c> (class <c>MailActivationFlagOffTests</c>). The
/// production default: <c>Email:TenantMail:Enabled=false</c>. Activation and app passwords must
/// refuse; status must answer with enabled=false.
/// </summary>
/// <remarks>
/// Declares no <see cref="V2Fixture.ConfigOverrides"/> at all, which is the whole point — and where
/// the V1 original depended on its sibling's <c>[TearDown]</c> having cleared the process-wide
/// environment variables first, this fixture is isolated by construction and could run beside it.
/// </remarks>
[TestFixture]
public class MailActivationFlagOffTests : V2Fixture
{
    [Test]
    public async Task ItShouldRefuseActivationWhileTenantMailIsDisabled()
    {
        var owner = await LoginAsOwner();
        var client = owner.RefitFor<IMailTestHttpClientForOwner>();

        var status = await client.GetStatus();
        Assert.That(status.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(status.Content!.TenantMailEnabled, Is.False);
        Assert.That(status.Content.Activated, Is.False);

        var material = OpenPgpKeyManagement.GenerateP384KeyMaterial("frodo@frodo.dotyou.cloud");
        var activation = await client.Activate(new ActivateMailRequest
        {
            PublicCertificateArmored = material.PublicCertificateArmored,
            PrimaryEmailAddress = "frodo@frodo.dotyou.cloud",
        });
        Assert.That(activation.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }
}
