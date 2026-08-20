using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Cryptography.Pgp;
using Odin.Core.Serialization;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Mail;
using Odin.Services.Fingering;

namespace Odin.Hosting.Tests.OwnerApi.Mail;

// The activation flow (docs/email-keys-plan.md) end to end with tenant mail ENABLED
// (env overrides below; the production default stays off - flag-off behavior is
// asserted separately in MailActivationFlagOffTests).
public class MailActivationTests
{
    private const string Domain = "frodo.dotyou.cloud";
    private const string PrimaryAddress = "frodo@frodo.dotyou.cloud";

    private static readonly Dictionary<string, string> EnvOverrides = new()
    {
        { "Email__TenantMail__Enabled", "true" },
        { "Email__TenantMail__MxNodes__0", "mx1.dotyou.cloud" },
        { "Email__TenantMail__MxNodes__1", "mx2.dotyou.cloud" },
        { "Email__TenantMail__SpfIncludeTarget", "_spf.dotyou.cloud" },
        { "Email__TenantMail__DmarcReportEmail", "dmarc-reports@dotyou.cloud" },
        { "Email__TenantMail__TlsReportEmail", "tls-reports@dotyou.cloud" },
        { "Email__DkimStorageKey", "BAADF00DBAADF00DBAADF00DBAADF00DBAADF00DBAADF00DBAADF00DBAADF00D" },
    };

    private WebScaffold _scaffold = null!;

    [SetUp]
    public void Init()
    {
        var folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(envOverrides: EnvOverrides, testIdentities: [TestIdentities.Frodo]);

        // TenantMail enabled with Provider=None makes the startup verifier log its
        // deliberate config ERR; that exact one is expected here, anything else is not
        _scaffold.SetAssertLogEventsAction(logEvents =>
        {
            var errors = logEvents[Serilog.Events.LogEventLevel.Error];
            Assert.That(errors.Count, Is.EqualTo(1), "Unexpected number of Error log events");
            Assert.That(errors[0].MessageTemplate.Text, Does.StartWith("Email:TenantMail:Enabled is true but Email:Provider is 'None'"));
        });
    }

    [TearDown]
    public void Cleanup()
    {
        _scaffold.RunAfterAnyTests();
        // Env vars are process-wide: without this, TenantMail would stay enabled for
        // every later fixture and break the flag-off assertions
        foreach (var key in EnvOverrides.Keys)
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    //

    [Test]
    public async Task ItShouldActivateIdempotentlyAndReportStatus()
    {
        var client = new MailApiClient(_scaffold.OldOwnerApi, TestIdentities.Frodo);

        // Before: enabled but not activated
        var statusBefore = await client.GetStatus();
        Assert.That(statusBefore.IsSuccessStatusCode, Is.True);
        Assert.That(statusBefore.Content!.TenantMailEnabled, Is.True);
        Assert.That(statusBefore.Content.Activated, Is.False);
        Assert.That(statusBefore.Content.DkimRecords, Is.Empty);

        // Activate
        var material = OpenPgpKeyManagement.GenerateP384KeyMaterial(PrimaryAddress);
        var activation = await client.Activate(material.PublicCertificateArmored, PrimaryAddress);
        Assert.That(activation.IsSuccessStatusCode, Is.True, activation.Error?.Content);

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
        var secondActivation = await client.Activate(material.PublicCertificateArmored, PrimaryAddress);
        Assert.That(secondActivation.IsSuccessStatusCode, Is.True);
        Assert.That(secondActivation.Content!.DkimRecords.Select(r => r.Value),
            Is.EquivalentTo(result.DkimRecords.Select(r => r.Value)));

        // The anonymous surfaces went live: WKD serves, DID gains keyAgreement,
        // autoconfig answers (flag is on here)
        var anonymousClient = WebScaffold.HttpClientFactory.CreateClient($"{Domain}:4444");

        var wkd = await anonymousClient.GetAsync($"https://{Domain}:4444/.well-known/openpgpkey/hu/anyhash");
        Assert.That(wkd.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var did = await anonymousClient.GetAsync($"https://{Domain}:4444/.well-known/did.json");
        var didDoc = OdinSystemSerializer.Deserialize<DidWebResponse>(await did.Content.ReadAsStringAsync());
        Assert.That(didDoc!.KeyAgreement, Is.Not.Null.And.Not.Empty);

        var autoconfig = await anonymousClient.GetAsync($"https://{Domain}:4444/.well-known/autoconfig/mail/config-v1.1.xml");
        Assert.That(autoconfig.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var xml = await autoconfig.Content.ReadAsStringAsync();
        Assert.That(xml, Does.Contain("<hostname>mx1.dotyou.cloud</hostname>"));
        Assert.That(xml, Does.Contain("<hostname>mx2.dotyou.cloud</hostname>"));
    }

    [Test]
    public async Task ItShouldProvisionAnAppPasswordOnceActivated()
    {
        var client = new MailApiClient(_scaffold.OldOwnerApi, TestIdentities.Frodo);

        // Not activated yet -> refused
        var refused = await client.ProvisionAppPassword(PrimaryAddress, "Thunderbird");
        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var material = OpenPgpKeyManagement.GenerateP384KeyMaterial(PrimaryAddress);
        var activation = await client.Activate(material.PublicCertificateArmored, PrimaryAddress);
        Assert.That(activation.IsSuccessStatusCode, Is.True);

        var response = await client.ProvisionAppPassword(PrimaryAddress, "Thunderbird");
        Assert.That(response.IsSuccessStatusCode, Is.True);
        Assert.That(response.Content!.Password, Does.Match(new Regex("^[a-z2-7]{5}(-[a-z2-7]{5}){3}$")));

        // Each provisioning yields a fresh password
        var second = await client.ProvisionAppPassword(PrimaryAddress, "FairEmail");
        Assert.That(second.Content!.Password, Is.Not.EqualTo(response.Content.Password));
    }

    [Test]
    public async Task ItShouldRoundTripTheChallengeAndAnswerVerify()
    {
        var client = new MailApiClient(_scaffold.OldOwnerApi, TestIdentities.Frodo);

        // Not activated -> challenge refused
        var refused = await client.CreateChallenge();
        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var material = OpenPgpKeyManagement.GenerateP384KeyMaterial(PrimaryAddress);
        var activation = await client.Activate(material.PublicCertificateArmored, PrimaryAddress);
        Assert.That(activation.IsSuccessStatusCode, Is.True);

        // The full owner round-trip check, exactly as the app performs it: decrypt the
        // challenge with the private keyring and compare hashes
        var challenge = await client.CreateChallenge();
        Assert.That(challenge.IsSuccessStatusCode, Is.True);

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
        Assert.That(verify.IsSuccessStatusCode, Is.True);
        Assert.That(verify.Content!.Activated, Is.True);
    }

    [Test]
    public async Task ItShouldRejectBadInput()
    {
        var client = new MailApiClient(_scaffold.OldOwnerApi, TestIdentities.Frodo);
        var material = OpenPgpKeyManagement.GenerateP384KeyMaterial(PrimaryAddress);

        // Foreign address
        var foreignAddress = await client.Activate(material.PublicCertificateArmored, "frodo@sam.dotyou.cloud");
        Assert.That(foreignAddress.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        // Garbage certificate
        var garbage = await client.Activate("not a certificate", PrimaryAddress);
        Assert.That(garbage.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        // A secret keyring must be refused before anything is stored
        var secret = await client.Activate(material.SecretKeyArmored, PrimaryAddress);
        Assert.That(secret.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var status = await client.GetStatus();
        Assert.That(status.Content!.Activated, Is.False, "nothing may be activated after refusals");
    }
}

// The production default: Email:TenantMail:Enabled=false. Activation and app
// passwords must refuse; status must answer with enabled=false.
public class MailActivationFlagOffTests
{
    private WebScaffold _scaffold = null!;

    [SetUp]
    public void Init()
    {
        var folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: [TestIdentities.Frodo]);
    }

    [TearDown]
    public void Cleanup()
    {
        _scaffold.RunAfterAnyTests();
    }

    [Test]
    public async Task ItShouldRefuseActivationWhileTenantMailIsDisabled()
    {
        var client = new MailApiClient(_scaffold.OldOwnerApi, TestIdentities.Frodo);

        var status = await client.GetStatus();
        Assert.That(status.IsSuccessStatusCode, Is.True);
        Assert.That(status.Content!.TenantMailEnabled, Is.False);
        Assert.That(status.Content.Activated, Is.False);

        var material = OpenPgpKeyManagement.GenerateP384KeyMaterial("frodo@frodo.dotyou.cloud");
        var activation = await client.Activate(material.PublicCertificateArmored, "frodo@frodo.dotyou.cloud");
        Assert.That(activation.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }
}
