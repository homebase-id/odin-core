using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using DnsClient;
using DnsClient.Protocol;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Pgp;
using Odin.Core.Http;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Factory;
using Odin.Core.Util;
using Odin.Services.Base;
using Odin.Services.Email;
using Odin.Services.Email.Dkim;
using Odin.Test.Helpers;

namespace Odin.Services.Tests.Email;

// Unattended email health checks: DKIM pair proof against (mocked) live DNS and
// public-key drift across the (mocked) publication surfaces.
[TestFixture]
public class EmailHealthVerifierTest
{
    private const string Domain = "frodo.dotyou.cloud";

    private string _tempDir = "";
    private TestServices? _testServices;
    private EmailPublicKeyService _emailPublicKeyService = null!;
    private readonly Mock<IDkimStore> _dkimStore = new(MockBehavior.Loose);
    private readonly Mock<ILookupClient> _dnsClient = new(MockBehavior.Loose);
    private readonly Mock<IDynamicHttpClientFactory> _httpClientFactory = new(MockBehavior.Loose);

    [SetUp]
    public void Setup()
    {
        _tempDir = TempDirectory.Create();
        _testServices = new TestServices();
        _dkimStore.Reset();
        _dnsClient.Reset();
        _httpClientFactory.Reset();
    }

    [TearDown]
    public void TearDown()
    {
        _testServices?.Dispose();
        Directory.Delete(_tempDir, true);
    }

    //

    private async Task<EmailHealthVerifier> CreateVerifierAsync()
    {
        var services = await _testServices!.RegisterServicesAsync(DatabaseType.Sqlite, _tempDir, Guid.NewGuid());
        _emailPublicKeyService = new EmailPublicKeyService(services.Resolve<IdentityDatabase>());

        var tenantContext = new TenantContext(
            Guid.NewGuid(), new OdinId(Domain), null!,
            firstRunToken: null, isPreconfigured: true, markedForDeletionDate: null, email: null);

        return new EmailHealthVerifier(
            new Mock<ILogger<EmailHealthVerifier>>().Object,
            tenantContext,
            _dkimStore.Object,
            _emailPublicKeyService,
            _dnsClient.Object,
            _httpClientFactory.Object);
    }

    private void SetupDkimKeys(List<DkimKey> keys)
    {
        _dkimStore.SetupGet(x => x.IsConfigured).Returns(true);
        _dkimStore.Setup(x => x.GetKeysAsync(Domain)).ReturnsAsync(keys);
    }

    private void SetupTxt(string owner, params string[][] txtChunks)
    {
        var info = () => new ResourceRecordInfo(DnsString.Parse(owner), ResourceRecordType.TXT, QueryClass.IN, 3600, 0);
        var answers = txtChunks.Select(chunks => (DnsResourceRecord)new TxtRecord(info(), chunks, chunks)).ToArray();
        var response = new Mock<IDnsQueryResponse>();
        response.SetupGet(x => x.HasError).Returns(false);
        response.SetupGet(x => x.Answers).Returns(answers);
        _dnsClient
            .Setup(x => x.QueryAsync(owner, QueryType.TXT, It.IsAny<QueryClass>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);
    }

    private void SetupSurfaces(byte[]? wkdBody, string? didBody, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new DispatchingHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains("/.well-known/openpgpkey/"))
            {
                return wkdBody == null
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : new HttpResponseMessage(status) { Content = new ByteArrayContent(wkdBody) };
            }
            if (url.Contains("did.json"))
            {
                return didBody == null
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : new HttpResponseMessage(status) { Content = new StringContent(didBody) };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        _httpClientFactory
            .Setup(x => x.CreateClient(It.IsAny<string>(), It.IsAny<Action<ClientHandlerConfig>?>()))
            .Returns(new HttpClient(handler));
    }

    private static string DidBodyFor(string publicCertificateArmored)
    {
        var spkiDer = OpenPgpKeyManagement.GetEncryptionSubkeySpkiDer(publicCertificateArmored);
        var jwk = new EccPublicKeyData { publicKey = spkiDer }.PublicKeyJwk();
        return $"{{\"keyAgreement\":[\"did:web:{Domain}#key-agreement\"],\"verificationMethod\":[{{\"publicKeyJwk\":{jwk}}}]}}";
    }

    //

    [Test]
    public async Task ItShouldReportNotActivatedAndTouchNothing()
    {
        var verifier = await CreateVerifierAsync();

        var result = await verifier.VerifyAsync(CancellationToken.None);

        Assert.That(result.Activated, Is.False);
        Assert.That(result.IsClean, Is.True);
        _dkimStore.VerifyNoOtherCalls();
        _dnsClient.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ItShouldPassWhenDnsAndSurfacesMatch()
    {
        var verifier = await CreateVerifierAsync();
        var material = OpenPgpKeyManagement.GenerateP384KeyMaterial($"frodo@{Domain}");
        await _emailPublicKeyService.PublishAsync(material.PublicCertificateArmored);

        var keys = DkimKeyGenerator.GenerateKeys();
        SetupDkimKeys(keys);
        foreach (var key in keys)
        {
            SetupTxt($"{key.DnsRecordName}.{Domain}", [key.DnsRecordValue]);
        }
        SetupSurfaces(
            OpenPgpKeyManagement.GetPublicCertificateBinary(material.PublicCertificateArmored),
            DidBodyFor(material.PublicCertificateArmored));

        var result = await verifier.VerifyAsync(CancellationToken.None);

        Assert.That(result.Activated, Is.True);
        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.Warnings, Is.Empty);
    }

    [Test]
    public async Task ItShouldReassembleChunkedDkimRecords()
    {
        var verifier = await CreateVerifierAsync();
        var material = OpenPgpKeyManagement.GenerateP384KeyMaterial($"frodo@{Domain}");
        await _emailPublicKeyService.PublishAsync(material.PublicCertificateArmored);

        var keys = DkimKeyGenerator.GenerateKeys();
        SetupDkimKeys(keys);
        foreach (var key in keys)
        {
            // The rsa record exceeds 255 bytes in real DNS - serve every record chunked
            var value = key.DnsRecordValue;
            var mid = value.Length / 2;
            SetupTxt($"{key.DnsRecordName}.{Domain}", [value[..mid], value[mid..]]);
        }
        SetupSurfaces(
            OpenPgpKeyManagement.GetPublicCertificateBinary(material.PublicCertificateArmored),
            DidBodyFor(material.PublicCertificateArmored));

        var result = await verifier.VerifyAsync(CancellationToken.None);

        Assert.That(result.Errors, Is.Empty);
    }

    [Test]
    public async Task ItShouldFailThePairProofWhenDnsServesAForeignKey()
    {
        var verifier = await CreateVerifierAsync();
        var material = OpenPgpKeyManagement.GenerateP384KeyMaterial($"frodo@{Domain}");
        await _emailPublicKeyService.PublishAsync(material.PublicCertificateArmored);

        var keys = DkimKeyGenerator.GenerateKeys();
        var foreignKeys = DkimKeyGenerator.GenerateKeys();
        SetupDkimKeys(keys);
        for (var i = 0; i < keys.Count; i++)
        {
            // Same selector, same algorithm - but a different keypair's public half
            SetupTxt($"{keys[i].DnsRecordName}.{Domain}", [foreignKeys[i].DnsRecordValue]);
        }
        SetupSurfaces(
            OpenPgpKeyManagement.GetPublicCertificateBinary(material.PublicCertificateArmored),
            DidBodyFor(material.PublicCertificateArmored));

        var result = await verifier.VerifyAsync(CancellationToken.None);

        Assert.That(result.Errors.Count, Is.EqualTo(2));
        Assert.That(result.Errors, Has.All.Contains("pair proof FAILED"));
    }

    [Test]
    public async Task ItShouldReportMissingDkimRecordsAndMissingKeys()
    {
        var verifier = await CreateVerifierAsync();
        var material = OpenPgpKeyManagement.GenerateP384KeyMaterial($"frodo@{Domain}");
        await _emailPublicKeyService.PublishAsync(material.PublicCertificateArmored);

        var keys = DkimKeyGenerator.GenerateKeys();
        SetupDkimKeys(keys);
        foreach (var key in keys)
        {
            SetupTxt($"{key.DnsRecordName}.{Domain}"); // no answers
        }
        SetupSurfaces(
            OpenPgpKeyManagement.GetPublicCertificateBinary(material.PublicCertificateArmored),
            DidBodyFor(material.PublicCertificateArmored));

        var result = await verifier.VerifyAsync(CancellationToken.None);
        Assert.That(result.Errors.Count, Is.EqualTo(2));
        Assert.That(result.Errors, Has.All.Contains("missing"));

        // Activated but zero stored keys is its own error
        _dkimStore.Setup(x => x.GetKeysAsync(Domain)).ReturnsAsync([]);
        var noKeys = await verifier.VerifyAsync(CancellationToken.None);
        Assert.That(noKeys.Errors.Single(), Does.Contain("no DKIM keys"));
    }

    [Test]
    public async Task ItShouldDetectDriftOnBothSurfaces()
    {
        var verifier = await CreateVerifierAsync();
        var material = OpenPgpKeyManagement.GenerateP384KeyMaterial($"frodo@{Domain}");
        var impostor = OpenPgpKeyManagement.GenerateP384KeyMaterial($"frodo@{Domain}");
        await _emailPublicKeyService.PublishAsync(material.PublicCertificateArmored);

        var keys = DkimKeyGenerator.GenerateKeys();
        SetupDkimKeys(keys);
        foreach (var key in keys)
        {
            SetupTxt($"{key.DnsRecordName}.{Domain}", [key.DnsRecordValue]);
        }
        // Both surfaces serve a DIFFERENT certificate than the published one
        SetupSurfaces(
            OpenPgpKeyManagement.GetPublicCertificateBinary(impostor.PublicCertificateArmored),
            DidBodyFor(impostor.PublicCertificateArmored));

        var result = await verifier.VerifyAsync(CancellationToken.None);

        Assert.That(result.Errors.Count, Is.EqualTo(2));
        Assert.That(result.Errors, Has.All.Contains("drift"));
    }

    [Test]
    public async Task ItShouldWarnNotErrorWhenSurfacesAreUnreachable()
    {
        var verifier = await CreateVerifierAsync();
        var material = OpenPgpKeyManagement.GenerateP384KeyMaterial($"frodo@{Domain}");
        await _emailPublicKeyService.PublishAsync(material.PublicCertificateArmored);

        var keys = DkimKeyGenerator.GenerateKeys();
        SetupDkimKeys(keys);
        foreach (var key in keys)
        {
            SetupTxt($"{key.DnsRecordName}.{Domain}", [key.DnsRecordValue]);
        }
        SetupSurfaces(wkdBody: null, didBody: null); // both 404

        var result = await verifier.VerifyAsync(CancellationToken.None);

        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.Warnings.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task ItShouldSkipDkimWithAWarningWhenStoreIsNotConfigured()
    {
        var verifier = await CreateVerifierAsync();
        var material = OpenPgpKeyManagement.GenerateP384KeyMaterial($"frodo@{Domain}");
        await _emailPublicKeyService.PublishAsync(material.PublicCertificateArmored);

        _dkimStore.SetupGet(x => x.IsConfigured).Returns(false);
        SetupSurfaces(
            OpenPgpKeyManagement.GetPublicCertificateBinary(material.PublicCertificateArmored),
            DidBodyFor(material.PublicCertificateArmored));

        var result = await verifier.VerifyAsync(CancellationToken.None);

        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.Warnings.Single(), Does.Contain("DkimStorageKey"));
        _dkimStore.Verify(x => x.GetKeysAsync(It.IsAny<string>()), Times.Never);
    }

    //

    private class DispatchingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responder(request));
        }
    }
}
