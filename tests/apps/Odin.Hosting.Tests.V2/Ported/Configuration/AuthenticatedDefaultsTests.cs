using System;
using System.Collections.Generic;
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
/// already initializes, and neither test asserted anything about that call's response, so it is
/// dropped rather than repeated — nothing here asserts on the un-initialized state either, so
/// <see cref="V2Fixture.InitializeIdentities"/> stays true (contrast
/// <see cref="SystemInitializeConfigTests"/>, whose subject <i>is</i> initial setup).
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
    [TestCase("AuthenticatedIdentitiesCanReactOnAnonymousDrives", true)]
    [TestCase("AuthenticatedIdentitiesCanCommentOnAnonymousDrives", false)]
    public async Task SystemDefault_TenantSettings_IsExpected(string setting, bool expected)
    {
        var read = Readers[setting];

        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitOwnerConfiguration>();

        var getSettingsResponse = await svc.GetTenantSettings();
        Assert.That(getSettingsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(read(getSettingsResponse.Content!), Is.EqualTo(expected));
    }

    /// <summary>Keyed by name, because the <c>[TestCase]</c> label is what a failure prints.</summary>
    private static readonly Dictionary<string, Func<TenantSettings, bool>> Readers = new()
    {
        ["AuthenticatedIdentitiesCanReactOnAnonymousDrives"] = s => s.AuthenticatedIdentitiesCanReactOnAnonymousDrives,
        ["AuthenticatedIdentitiesCanCommentOnAnonymousDrives"] = s => s.AuthenticatedIdentitiesCanCommentOnAnonymousDrives
    };
}
