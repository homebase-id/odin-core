using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.Configuration;
using Odin.Services.Configuration;

namespace Odin.Hosting.Tests.V2.Ported.Configuration;

/// <summary>
/// Port of <c>OwnerApi/Configuration/OwnerAppSettingsTests</c>. The owner console's own key/value
/// bag: it must read back as an empty (but non-null) map on a fresh identity, and round-trip what
/// the console writes into it.
/// </summary>
/// <remarks>
/// The settings endpoints are the system under test, so they go through
/// <see cref="Api.OwnerSession.RefitFor{T}"/> rather than <c>owner.Admin</c>, which throws on
/// non-2xx. No caller matrix in the original and none added — an <c>OwnerApi</c> fixture; the
/// <c>SetupCallerWithOwner</c> ordering caveat does not apply.
/// </remarks>
[TestFixture]
public class OwnerAppSettingsTests : V2Fixture
{
    [Test]
    public async Task CanGetAndReadOwnerAppSetting()
    {
        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitOwnerConfiguration>();

        var getOwnerAppSettingsEmpty = await svc.GetOwnerAppSettings();
        Assert.That(getOwnerAppSettingsEmpty.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            "system should return empty settings when first initialized");
        Assert.That(getOwnerAppSettingsEmpty.Content, Is.Not.Null,
            "system should return empty settings when first initialized");
        Assert.That(getOwnerAppSettingsEmpty.Content!.Settings, Is.Not.Null,
            "system should return empty settings when first initialized");

        var ownerSettings = new OwnerAppSettings
        {
            Settings = new Dictionary<string, string>
            {
                { "setting1", "value1" },
                { "setting2", "value2" }
            }
        };

        await svc.UpdateOwnerAppSetting(ownerSettings);
        var getSettingsResponse = await svc.GetOwnerAppSettings();
        Assert.That(getSettingsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(getSettingsResponse.Content, Is.Not.Null);

        Assert.That(getSettingsResponse.Content!.Settings, Is.EquivalentTo(ownerSettings.Settings));
    }
}
