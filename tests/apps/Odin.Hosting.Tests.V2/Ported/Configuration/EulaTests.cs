using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Time;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.Configuration;
using Odin.Services.Configuration.Eula;

namespace Odin.Hosting.Tests.V2.Ported.Configuration;

/// <summary>
/// Port of <c>OwnerApi/Configuration/SystemInit/EulaTests</c>. Signing the EULA clears the
/// "signature required" gate and lands in the signature history; a version the server doesn't
/// require is refused.
/// </summary>
/// <remarks>
/// The original booted an un-initialized tenant (Pippin). No override is needed here: EULA
/// signatures live in their own config key and <c>TenantConfigService.EnsureInitialOwnerSetupAsync</c>
/// never touches them, so the fixture's initialized baseline still starts with the signature
/// outstanding — verified by reading <c>TenantConfigService</c>.
///
/// The EULA endpoints are the system under test, so they go through
/// <see cref="Api.OwnerSession.RefitFor{T}"/>. <c>SignatureDate</c> is compared on
/// <c>.milliseconds</c>: <see cref="UnixTimeUtc"/> is not <c>IComparable</c>, so NUnit's ordering
/// constraints on the struct itself compile and throw at run time.
///
/// No caller matrix in the original and none added.
/// </remarks>
[TestFixture]
public class EulaTests : V2Fixture
{
    [Test]
    public async Task CanSignAndGetSignatureHistory()
    {
        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitOwnerConfiguration>();

        var eulaResponse = await svc.IsEulaSignatureRequired();
        Assert.That(eulaResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(eulaResponse.Content, Is.True);

        const string version = EulaSystemInfo.RequiredVersion;
        var signature = Guid.NewGuid().ToByteArray();
        await svc.MarkEulaSigned(new MarkEulaSignedRequest
        {
            Version = version,
            SignatureBytes = signature
        });

        var eulaResponse2 = await svc.IsEulaSignatureRequired();
        Assert.That(eulaResponse2.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(eulaResponse2.Content, Is.False);

        var getHistoryResponse = await svc.GetEulaSignatureHistory();
        Assert.That(getHistoryResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var history = getHistoryResponse.Content;
        Assert.That(history, Is.Not.Null);
        var eulaSignature = history!.SingleOrDefault(s => s.Version == EulaSystemInfo.RequiredVersion);
        Assert.That(eulaSignature, Is.Not.Null);

        Assert.That(eulaSignature!.SignatureBytes.Length, Is.EqualTo(signature.Length));

        var nowMs = UnixTimeUtc.Now();
        Assert.That(eulaSignature.SignatureDate.milliseconds, Is.LessThan(nowMs.milliseconds));
    }

    [Test]
    public async Task FailToSignInvalidEulaVersion()
    {
        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitOwnerConfiguration>();

        var version = Guid.NewGuid().ToString("N");
        var markSignedResponse = await svc.MarkEulaSigned(new MarkEulaSignedRequest
        {
            Version = version,
            SignatureBytes = Guid.NewGuid().ToByteArray()
        });

        Assert.That(markSignedResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }
}
