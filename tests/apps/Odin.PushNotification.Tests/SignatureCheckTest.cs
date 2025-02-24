using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.X509;

namespace Odin.PushNotification.Tests;

public class SignatureCheckTest
{
    [Test]
    public async Task ItShouldDownloadCertificate()
    {
        var logger = new Mock<ILogger<SignatureCheck>>();
        var signatureCheck = new SignatureCheck(logger.Object);
        var certicate = await signatureCheck.DownloadCertificate("www.google.com");
        ClassicAssert.NotNull(certicate);
        Assert.That(certicate?.NotAfter, Is.GreaterThan(DateTime.Now));
    }

    [Test]
    public async Task ItShouldSucceedDomainSignatureCheck()
    {
        const string domain = "asdjhas23745234675xcnxscbdkjsahdflkjdsfkjdsajdhkajsdbsajd.foo.bar";
        var logger = new Mock<ILogger<SignatureCheck>>();
        var selfSigned = X509Extensions.CreateSelfSignedEcDsaCertificate(domain);

        var signatureCheck = new SignatureCheck(logger.Object);
        signatureCheck.AddCertificate(domain, selfSigned);

        var messageId = Guid.NewGuid().ToString();
        var signature = selfSigned.CreateSignature(messageId);
        var isValid = await signatureCheck.Validate(domain, signature, messageId);

        ClassicAssert.True(isValid);
    }

    //

    [Test]
    public async Task ItShouldFailDomainSignatureCheck()
    {
        const string domain = "asdjhas23745234675xcnxscbdkjsahdflkjdsfkjdsajdhkajsdbsajd.foo.bar";
        var logger = new Mock<ILogger<SignatureCheck>>();
        var selfSigned = X509Extensions.CreateSelfSignedEcDsaCertificate(domain);

        var signatureCheck = new SignatureCheck(logger.Object);

        var messageId = Guid.NewGuid().ToString();
        var signature = selfSigned.CreateSignature(messageId);
        var isValid = await signatureCheck.Validate(domain, signature, messageId);

        ClassicAssert.False(isValid);
    }

    //

}
