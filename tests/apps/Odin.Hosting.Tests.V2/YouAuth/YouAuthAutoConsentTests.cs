#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;
using static Odin.Hosting.Tests.V2.YouAuth.YouAuthFlow;

namespace Odin.Hosting.Tests.V2.YouAuth;

/// <summary>
/// The hosting provider's own domains (<c>HostingProvider:AutoConsentDomains</c>, docs/hosting-plans.md) sign an
/// owner in without the consent screen; every other domain still asks.
/// </summary>
[TestFixture]
public class YouAuthAutoConsentTests : V2Fixture
{
    private const string ProviderDomain = "invites.example.com";
    private const string OtherDomain = "amazoom.org";

    protected override IReadOnlyDictionary<string, string?> ConfigOverrides => new Dictionary<string, string?>
    {
        // Mixed case and spaces in config: matched exactly once normalised
        ["HostingProvider:AutoConsentDomains:0"] = " Invites.Example.com ",
    };

    [Test]
    public async Task TheHostingProvidersDomainGoesStraightBackWithACode()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var request = DomainRequest(ProviderDomain, $"https://{ProviderDomain}/callback");

        var response = await AuthorizeAsync(Host, owner, request);

        var query = AssertRedirectsTo(response, $"https://{ProviderDomain}/callback");
        Assert.That(query.ContainsKey("salt"), Is.True, "the [080] callback, with no consent page in between");
    }

    [Test]
    public async Task AnyOtherDomainStillNeedsConsent()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var request = DomainRequest(OtherDomain, $"https://{OtherDomain}/callback");

        AssertRedirectsToConsent(await AuthorizeAsync(Host, owner, request));
    }
}
