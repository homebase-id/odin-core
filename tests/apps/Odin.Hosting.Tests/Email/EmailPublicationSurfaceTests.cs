using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Odin.Core.Cryptography.Pgp;
using Odin.Core.Serialization;
using Odin.Services.Email;
using Odin.Services.Fingering;
using Odin.Services.Tenant.Container;
using Org.BouncyCastle.Bcpg.OpenPgp;

namespace Odin.Hosting.Tests.Email;

// The anonymous publication surfaces from docs/email-keys-plan.md: WKD, the DID
// document's keyAgreement entry, and mail autoconfig. Until the activate-email API
// exists (PR-F), tests publish through the tenant's EmailPublicKeyService directly.
public class EmailPublicationSurfaceTests
{
    private const string Domain = "frodo.dotyou.cloud";

    private WebScaffold _scaffold = null!;

    [SetUp]
    public void Init()
    {
        var folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests();
    }

    [TearDown]
    public void Cleanup()
    {
        _scaffold.RunAfterAnyTests();
    }

    //

    private async Task PublishKeyAsync(string publicCertificateArmored)
    {
        var container = _scaffold.Services.GetRequiredService<IMultiTenantContainer>();
        await using var scope = container.GetTenantScope(Domain).BeginLifetimeScope();
        var service = scope.Resolve<EmailPublicKeyService>();
        await service.PublishAsync(publicCertificateArmored);
    }

    private static HttpClient CreateAnonymousClient() =>
        WebScaffold.HttpClientFactory.CreateClient($"{Domain}:4444");

    //

    [Test]
    public async Task WkdShouldServe404BeforeActivationAndTheBinaryCertificateAfter()
    {
        var client = CreateAnonymousClient();
        const string wkdUrl = $"https://{Domain}:4444/.well-known/openpgpkey/hu/some1hash3string5?l=frodo";

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
        Assert.That(System.Convert.ToHexString(ring.GetPublicKey().GetFingerprint()), Is.EqualTo(material.FingerprintHex));
    }

    [Test]
    public async Task WkdPolicyShouldAlwaysAnswer()
    {
        var client = CreateAnonymousClient();
        var response = await client.SendAsync(new HttpRequestMessage(
            HttpMethod.Get, $"https://{Domain}:4444/.well-known/openpgpkey/policy"));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task DidDocumentShouldGainKeyAgreementAfterActivation()
    {
        var client = CreateAnonymousClient();
        const string didUrl = $"https://{Domain}:4444/.well-known/did.json";

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

        var client = CreateAnonymousClient();
        var response = await client.SendAsync(new HttpRequestMessage(
            HttpMethod.Get, $"https://{Domain}:4444/.well-known/autoconfig/mail/config-v1.1.xml"));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
