using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;

namespace Odin.Hosting.Tests.V2.Examples;

/// <summary>
/// Owner-variant port of <c>_V2/Tests/Auth/AuthTests.CanVerifyTokenAndLogout</c>.
/// App and Guest variants need V1 admin endpoints (app registration, YouAuth domain registration)
/// over the in-process router — landing in a follow-up phase.
/// </summary>
[TestFixture]
public class OwnerAuthTests : V2Fixture
{
    [Test]
    public async Task CanVerifyTokenAndLogout()
    {
        var owner = await LoginAsOwner(Identities.Frodo);

        var verify = await owner.Auth.VerifyToken();
        Assert.That(verify.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var logout = await owner.Auth.Logout();
        Assert.That(logout.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var verifyAfter = await owner.Auth.VerifyToken();
        Assert.That(verifyAfter.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }
}
