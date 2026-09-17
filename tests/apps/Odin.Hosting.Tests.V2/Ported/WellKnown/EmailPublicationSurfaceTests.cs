#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Cryptography.Pgp;
using Odin.Core.Serialization;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Email;
using Odin.Services.Fingering;
using Org.BouncyCastle.Bcpg.OpenPgp;

namespace Odin.Hosting.Tests.V2.Ported.WellKnown;

/// <summary>
/// Port of <c>Email/EmailPublicationSurfaceTests</c>. The anonymous publication surfaces from
/// docs/email-keys-plan.md: WKD, the DID document's keyAgreement entry, and mail autoconfig. Until
/// the activate-email API exists (PR-F), tests publish through the tenant's
/// <see cref="EmailPublicKeyService"/> directly.
/// </summary>
/// <remarks>
/// Checked port.
/// <list type="bullet">
/// <item>The original built its <c>WebScaffold</c> in <c>[SetUp]</c>, so its four tests cost four
/// full Kestrel boots. One in-process host now serves all four; the per-test DB restore is what
/// keeps the "before activation" assertions honest, since publishing a key writes tenant state.</item>
/// <item><c>Email:TenantMail:Enabled</c> is pinned to <c>false</c> via <see cref="ConfigOverrides"/>
/// rather than left to the production default the original relied on implicitly — it is the subject
/// of <see cref="AutoconfigShouldStay404WhileTenantMailIsDisabled"/>, so it should be stated. Not an
/// environment variable: fixtures here run in parallel and env vars are process-wide.</item>
/// <item>Frodo is pinned because the assertions spell out <c>did:web:frodo.dotyou.cloud</c> and the
/// WKD local part <c>frodo</c>.</item>
/// <item>The <c>:4444</c> port in every original URL is gone — there is no wire here.</item>
/// </list>
/// </remarks>
[TestFixture]
public class EmailPublicationSurfaceTests : V2Fixture
{
    private static readonly string Domain = Identities.Frodo;

    protected override IReadOnlyDictionary<string, string?> ConfigOverrides =>
        new Dictionary<string, string?>
        {
            // The production default, stated explicitly: even an activated tenant must not advertise
            // mail servers while this is off.
            ["Email:TenantMail:Enabled"] = "false"
        };

    private async Task PublishKeyAsync(string publicCertificateArmored)
    {
        await using var scope = Host.GetTenantScope(Domain).BeginLifetimeScope();
        var service = scope.Resolve<EmailPublicKeyService>();
        await service.PublishAsync(publicCertificateArmored);
    }

    //

    [Test]
    public async Task WkdShouldServe404BeforeActivationAndTheBinaryCertificateAfter()
    {
        using var client = Host.CreateAnonymousClient(Domain);
        var wkdUrl = $"https://{Domain}/.well-known/openpgpkey/hu/some1hash3string5?l=frodo";

        var before = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, wkdUrl));
        Assert.That(before.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        var material = OpenPgpKeyManagement.GenerateP384KeyMaterial($"frodo@{Domain}");
        await PublishKeyAsync(material.PublicCertificateArmored);

        var after = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, wkdUrl));
        Assert.That(after.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(after.Content.Headers.ContentType!.MediaType, Is.EqualTo("application/octet-stream"));
        Assert.That(after.Headers.GetValues("Access-Control-Allow-Origin"), Does.Contain("*"));

        // The served bytes are the binary certificate with the expected fingerprint
        var bytes = await after.Content.ReadAsByteArrayAsync();
        var ring = new PgpPublicKeyRing(new MemoryStream(bytes));
        Assert.That(Convert.ToHexString(ring.GetPublicKey().GetFingerprint()), Is.EqualTo(material.FingerprintHex));
    }

    [Test]
    public async Task WkdPolicyShouldAlwaysAnswer()
    {
        using var client = Host.CreateAnonymousClient(Domain);
        var response = await client.SendAsync(new HttpRequestMessage(
            HttpMethod.Get, $"https://{Domain}/.well-known/openpgpkey/policy"));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task DidDocumentShouldGainKeyAgreementAfterActivation()
    {
        using var client = Host.CreateAnonymousClient(Domain);
        var didUrl = $"https://{Domain}/.well-known/did.json";

        var before = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, didUrl));
        var beforeDoc = OdinSystemSerializer.Deserialize<DidWebResponse>(await before.Content.ReadAsStringAsync());
        Assert.That(beforeDoc!.KeyAgreement, Is.Null, "no keyAgreement before activation");

        var material = OpenPgpKeyManagement.GenerateP384KeyMaterial($"frodo@{Domain}");
        await PublishKeyAsync(material.PublicCertificateArmored);

        var after = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, didUrl));
        var afterDoc = OdinSystemSerializer.Deserialize<DidWebResponse>(await after.Content.ReadAsStringAsync());

        Assert.That(afterDoc!.KeyAgreement, Is.EqualTo(new[] { $"did:web:{Domain}#key-agreement" }));

        var keyAgreementMethod = afterDoc.VerificationMethod!
            .Find(m => m.Id == $"did:web:{Domain}#key-agreement");
        Assert.That(keyAgreementMethod, Is.Not.Null);
        Assert.That(keyAgreementMethod!.Type, Is.EqualTo("JsonWebKey2020"));
        Assert.That(keyAgreementMethod.PublicKeyJwk!.Kty, Is.EqualTo("EC"));
        Assert.That(keyAgreementMethod.PublicKeyJwk.Crv, Is.EqualTo("P-384"));
        Assert.That(keyAgreementMethod.PublicKeyJwk.X, Is.Not.Empty);
        Assert.That(keyAgreementMethod.PublicKeyJwk.Y, Is.Not.Empty);
    }

    [Test]
    public async Task AutoconfigShouldStay404WhileTenantMailIsDisabled()
    {
        // The test environment runs with Email:TenantMail:Enabled=false (the production
        // default): even an activated tenant must not advertise mail servers
        var material = OpenPgpKeyManagement.GenerateP384KeyMaterial($"frodo@{Domain}");
        await PublishKeyAsync(material.PublicCertificateArmored);

        using var client = Host.CreateAnonymousClient(Domain);
        var response = await client.SendAsync(new HttpRequestMessage(
            HttpMethod.Get, $"https://{Domain}/.well-known/autoconfig/mail/config-v1.1.xml"));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
