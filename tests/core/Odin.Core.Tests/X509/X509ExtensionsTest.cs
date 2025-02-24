using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.X509;

namespace Odin.Core.Tests.X509;

public class X509ExtensionsTest
{
    [Test]
    public void ItShouldSignAndVerifySignature()
    {
        var certificate = X509Extensions.CreateSelfSignedEcDsaCertificate("example.com");
        const string data = "Hello, World!";

        var signature = certificate.CreateSignature(data);
        var match = certificate.VerifySignature(signature, data);
        ClassicAssert.IsTrue(match);
    }
}
