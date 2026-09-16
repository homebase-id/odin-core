using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.Configuration;
using Odin.Services.Configuration;

namespace Odin.Hosting.Tests.V2.Ported.Configuration;

/// <summary>
/// Port of <c>OwnerApi/Configuration/SystemInit/AuthenticatedDefaultsTests</c>. What a freshly
/// initialized tenant grants an authenticated (YouAuth) visitor by default: react on anonymous
/// drives yes, comment on anonymous drives no.
/// </summary>
/// <remarks>
/// The original ran on an un-initialized tenant (<c>RunBeforeAnyTests(initializeIdentity: false)</c>)
/// and called <c>InitializeIdentity</c> inside each test. <see cref="V2Fixture.WarmTenantBaselineAsync"/>
/// already initializes, and the call is idempotent on the server, so the in-test call is kept verbatim
/// rather than deleted — nothing here asserts on the un-initialized state, so no override is needed
/// (contrast <see cref="SystemInitializeConfigTests"/>, whose subject <i>is</i> initial setup).
/// Carried verbatim: the original pinned Merry; here the fixture default acts, because the identities
/// are structurally identical and only one is booted.
///
/// The two <c>[Ignore]</c>d YouAuth tests move as-is. No caller matrix in the original and none added.
/// </remarks>
[TestFixture]
public class AuthenticatedDefaultsTests : V2Fixture
{
    [Test]
    [Ignore("cannot automatically test until we have a login process for youauth")]
    public void CanAllowAuthenticatedVisitorsToViewConnections()
    {
        Assert.Inconclusive("TODO");
    }

    [Test]
    [Ignore("cannot automatically test until we have a login process for youauth")]
    public void CanBlockAuthenticatedVisitorsFromViewingConnections()
    {
        Assert.Inconclusive("TODO");
    }

    [Test]
    public async Task SystemDefault_TenantSettings_AuthenticatedIdentitiesCanReactOnAnonymousDrives_IsTrue()
    {
        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitOwnerConfiguration>();

        await svc.InitializeIdentity(new InitialSetupRequest());

        var getSettingsResponse = await svc.GetTenantSettings();
        Assert.That(getSettingsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(getSettingsResponse.Content!.AuthenticatedIdentitiesCanReactOnAnonymousDrives, Is.True);
    }

    [Test]
    public async Task SystemDefault_TenantSettings_AuthenticatedIdentitiesCan_NOT_CommentOnAnonymousDrives_IsTrue()
    {
        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitOwnerConfiguration>();

        await svc.InitializeIdentity(new InitialSetupRequest());

        var getSettingsResponse = await svc.GetTenantSettings();
        Assert.That(getSettingsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(getSettingsResponse.Content!.AuthenticatedIdentitiesCanCommentOnAnonymousDrives, Is.False);
    }
}
