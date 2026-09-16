using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests.Anonymous.Ident;
using Odin.Hosting.Tests.V2.Api;
using Refit;

namespace Odin.Hosting.Tests.V2.Ported.Ident;

/// <summary>
/// Port of <c>Anonymous/Ident/AppIdentTests</c>. An unauthenticated caller reads the tenant's
/// identity card from <c>/api/guest/v1/auth/ident</c>.
/// </summary>
/// <remarks>
/// Checked port.
/// <list type="bullet">
/// <item>No caller matrix in the original and none here — the endpoint is anonymous by definition.</item>
/// <item>The original pinned Samwise; nothing in the assertions names an identity, so this uses the
/// fixture default (see the README's "prefer the fixture default").</item>
/// <item>The status-code assertion is new: the original dereferenced <c>identResponse.Content</c>
/// without checking, so a non-2xx surfaced as a <c>NullReferenceException</c>.</item>
/// <item><c>IIdentHttpClient</c> still lives in the V1 test project (under the misleading file name
/// <c>Anonymous/Ident/IRefitHomeDriveQuery.cs</c>) because the Performance fixtures still use it.</item>
/// </list>
/// </remarks>
[TestFixture]
public class AppIdentTests : V2Fixture
{
    [Test]
    public async Task CanGetIdentInfo()
    {
        using var anonClient = Host.CreateAnonymousClient(Identities.Frodo);
        var svc = RestService.For<IIdentHttpClient>(anonClient);

        var identResponse = await svc.GetIdent();
        Assert.That(identResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var ident = identResponse.Content!;
        Assert.That(ident.OdinId, Is.Not.Empty);
        Assert.That(ident.Version, Is.EqualTo(1.0));
    }
}
