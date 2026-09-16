using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.Configuration;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Configuration;
using Odin.Services.Membership.Circles;

namespace Odin.Hosting.Tests.V2.Ported.Configuration;

/// <summary>
/// Port of <c>OwnerApi/Configuration/SystemInit/ConnectedIdentityDefaultsTests</c>. What a connected
/// identity may do by default, and the one flag whose flip rewrites a system circle:
/// <c>ConnectedIdentitiesCanViewConnections</c> adds / removes <c>ReadConnections</c> on the
/// confirmed-connections circle.
/// </summary>
/// <remarks>
/// As with <see cref="AuthenticatedDefaultsTests"/>, the original started from an un-initialized
/// tenant and called <c>InitializeIdentity</c> per test; the fast fixture already initializes in its
/// baseline and the call is idempotent, so the in-test call is carried rather than dropped. Nothing
/// here asserts on the pre-init state, so no <see cref="V2Fixture.WarmTenantBaselineAsync"/> override.
///
/// Two deviations, both checked:
/// <list type="bullet">
/// <item>The flag update went through <c>Configuration.UpdateTenantSettingsFlag</c>, which asserted
/// success internally. Here it is arrange, so it goes through <c>owner.Admin.UpdateTenantSettingsFlag</c>,
/// which throws on non-2xx — the same claim in the framework's idiom.</item>
/// <item>Reading the system circle back used the list-and-assert-success shape; the README prefers the
/// single-entity helper, so it is <c>owner.Admin.GetCircleDefinition</c> (which likewise throws rather
/// than returning a status to assert on). The circle is read, not written, by this fixture — the SUT
/// is the flag endpoint's side effect.</item>
/// </list>
///
/// Carried verbatim, including the defect: <c>SystemDefault_TenantSettings_AutoAcceptIntroductions_IsTrue</c>
/// does not assert anything about auto-accept — it asserts
/// <c>ConnectedIdentitiesCanReactOnAnonymousDrives</c>, exactly as the test above it does. It is a
/// duplicate under a misleading name. Left alone: fixing behaviour inside a port makes the diff
/// unreviewable (see #1767).
///
/// The three <c>[Ignore]</c>d YouAuth tests move as-is. No caller matrix in the original and none added.
/// </remarks>
[TestFixture]
public class ConnectedIdentityDefaultsTests : V2Fixture
{
    [Test]
    [Ignore("cannot automatically test until we have a login process for youauth")]
    public void SystemDefault_ConnectedContactsCannotViewConnections()
    {
    }

    [Test]
    [Ignore("cannot automatically test until we have a login process for youauth")]
    public void CanAllowConnectedContactsToViewConnections()
    {
    }

    [Test]
    [Ignore("cannot automatically test until we have a login process for youauth")]
    public void CanBlockConnectedContactsFromViewingConnectionsUnlessInCircle()
    {
        Assert.Inconclusive("TODO");
    }

    [Test]
    public async Task SystemCircleUpdatedWhenConnectedFlagChanges()
    {
        var owner = await LoginAsOwner();
        var config = owner.RefitFor<IRefitOwnerConfiguration>();

        var initResponse = await config.InitializeIdentity(new InitialSetupRequest
        {
            Drives = null,
            Circles = null
        });

        Assert.That(initResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(initResponse.Content, Is.True);

        await owner.Admin.UpdateTenantSettingsFlag(
            TenantConfigFlagNames.ConnectedIdentitiesCanViewConnections, bool.TrueString);

        var systemCircle1 = await owner.Admin.GetCircleDefinition(SystemCircleConstants.ConfirmedConnectionsCircleId.Value);
        Assert.That(systemCircle1.Permissions.Keys, Does.Contain(PermissionKeys.ReadConnections));

        //
        // Disable ability to read connections
        //
        await owner.Admin.UpdateTenantSettingsFlag(
            TenantConfigFlagNames.ConnectedIdentitiesCanViewConnections, bool.FalseString);

        //
        // system circle should not have permissions
        //
        var systemCircle = await owner.Admin.GetCircleDefinition(SystemCircleConstants.ConfirmedConnectionsCircleId.Value);
        Assert.That(systemCircle.Permissions.Keys, Does.Not.Contain(PermissionKeys.ReadConnections));
    }

    [Test]
    public async Task SystemDefault_TenantSettings_ConnectedIdentitiesCanReactOnAnonymousDrives_IsTrue()
    {
        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitOwnerConfiguration>();

        await svc.InitializeIdentity(new InitialSetupRequest());

        var getSettingsResponse = await svc.GetTenantSettings();
        Assert.That(getSettingsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(getSettingsResponse.Content!.ConnectedIdentitiesCanReactOnAnonymousDrives, Is.True);
    }

    [Test]
    public async Task SystemDefault_TenantSettings_AutoAcceptIntroductions_IsTrue()
    {
        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitOwnerConfiguration>();

        await svc.InitializeIdentity(new InitialSetupRequest());

        var getSettingsResponse = await svc.GetTenantSettings();
        Assert.That(getSettingsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Carried defect: the name says auto-accept-introductions, the assertion reads the
        // react-on-anonymous-drives flag. Verbatim from the original.
        Assert.That(getSettingsResponse.Content!.ConnectedIdentitiesCanReactOnAnonymousDrives, Is.True);
    }

    [Test]
    public async Task SystemDefault_TenantSettings_ConnectedIdentitiesCanCommentOnAnonymousDrives_IsTrue()
    {
        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitOwnerConfiguration>();

        await svc.InitializeIdentity(new InitialSetupRequest());

        var getSettingsResponse = await svc.GetTenantSettings();
        Assert.That(getSettingsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(getSettingsResponse.Content!.ConnectedIdentitiesCanCommentOnAnonymousDrives, Is.True);
    }
}
