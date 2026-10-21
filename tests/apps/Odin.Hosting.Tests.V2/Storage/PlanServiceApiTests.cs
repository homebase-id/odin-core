#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Hosting.Controllers.PlanService;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Hosting;

namespace Odin.Hosting.Tests.V2.Storage;

/// <summary>
/// The plan service API (docs/hosting-plans.md section 9): homebase-invites reads and sets an identity's plan, on
/// the provisioning domain, with the plan service key and nothing else.
/// </summary>
[TestFixture]
public class PlanServiceApiTests : V2Fixture
{
    private const string Key = "plan-service-test-key";
    private const string ProvisioningDomain = "provisioning.dotyou.cloud";

    // The test identities' own plan (DevEnvironmentSetup); configured, so the teardown can put it back
    private const string DevPlan = "dev-domain";

    protected override IReadOnlyDictionary<string, string?> ConfigOverrides => new Dictionary<string, string?>
    {
        ["Registry:Plans:0:Id"] = "free",
        ["Registry:Plans:0:Default"] = "true",
        ["Registry:Plans:1:Id"] = "veterans",
        ["Registry:Plans:1:QuotaBytes"] = "1099511627776",
        ["Registry:Plans:2:Id"] = DevPlan,
        ["Registry:PlanService:ApiKey"] = Key,
    };

    /// <summary>
    /// The plan lives in the identity registry, which the per-test reset does not restore
    /// </summary>
    [TearDown]
    public async Task RestorePlan()
    {
        await SetPlanAsync(Identities.Frodo.ToString(), DevPlan);
    }

    [Test]
    public async Task TheServiceReadsAnIdentitysPlan()
    {
        var response = await SendAsync(HttpMethod.Get, $"identities/{Identities.Frodo}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var identity = await ReadAsync(response);
        Assert.That(identity.Domain, Is.EqualTo(Identities.Frodo.ToString()));
        Assert.That(identity.PlanId, Is.EqualTo(DevPlan));
    }

    [Test]
    public async Task TheServiceSetsAPlan_AndSettingItAgainChangesNothing()
    {
        var first = await SetPlanAsync(Identities.Frodo.ToString(), "veterans", "payment-1");
        var again = await SetPlanAsync(Identities.Frodo.ToString(), "veterans", "payment-1");

        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.OK), "a retry is fine");
        Assert.That((await ReadAsync(again)).PlanId, Is.EqualTo("veterans"));
        Assert.That((await ReadAsync(await SendAsync(HttpMethod.Get, $"identities/{Identities.Frodo}"))).PlanId,
            Is.EqualTo("veterans"));
    }

    [Test]
    public async Task APlanNotConfiguredHereIsRefused()
    {
        var response = await SetPlanAsync(Identities.Frodo.ToString(), "no-such-plan");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task AnIdentityNotOnThisHostIsNotFound()
    {
        Assert.That((await SendAsync(HttpMethod.Get, "identities/no-such-tenant.dotyou.cloud")).StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await SetPlanAsync("no-such-tenant.dotyou.cloud", "veterans")).StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound));
    }

    [TestCase(null)]
    [TestCase("wrong-key")]
    public async Task WithoutTheKeyItIsUnauthorized(string? key)
    {
        var response = await SendAsync(HttpMethod.Get, $"identities/{Identities.Frodo}", key: key);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task OnAnIdentitysOwnDomainItIsNotThere()
    {
        var response = await SendAsync(HttpMethod.Get, $"identities/{Identities.Frodo}", origin: Identities.Frodo.ToString());

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    //

    private Task<HttpResponseMessage> SetPlanAsync(string domain, string planId, string? reference = null) =>
        SendAsync(HttpMethod.Put, $"identities/{domain}/plan",
            OdinSystemSerializer.Serialize(new SetPlanServicePlanRequest { PlanId = planId, Reference = reference }));

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? json = null,
        string? key = Key, string origin = ProvisioningDomain)
    {
        using var client = new HttpClient(Host.Server.CreateHandler()) { BaseAddress = new Uri($"https://{origin}/") };
        var request = new HttpRequestMessage(method, $"api/plans/v1/{path}");
        if (key != null)
        {
            request.Headers.Add(PlanServiceApiPathConstants.ApiKeyHeader, key);
        }
        if (json != null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return await client.SendAsync(request);
    }

    private static async Task<PlanServiceIdentity> ReadAsync(HttpResponseMessage response) =>
        OdinSystemSerializer.Deserialize<PlanServiceIdentity>(await response.Content.ReadAsStringAsync())!;
}

/// <summary>
/// Without a plan service key the API is not there at all.
/// </summary>
[TestFixture]
public class PlanServiceApiOffTests : V2Fixture
{
    [Test]
    public async Task WithoutAKeyConfiguredItIsNotFound()
    {
        using var client = new HttpClient(Host.Server.CreateHandler()) { BaseAddress = new Uri("https://provisioning.dotyou.cloud/") };
        var request = new HttpRequestMessage(HttpMethod.Get, $"api/plans/v1/identities/{Identities.Frodo}");
        request.Headers.Add(PlanServiceApiPathConstants.ApiKeyHeader, "anything");

        var response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
