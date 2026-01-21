using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Odin.Core.Http;
using Odin.Hosting.UnifiedV2;

namespace Odin.Hosting.Tests._V2.Tests.Ping;

public class PingTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities:
        [
            TestIdentities.Samwise
        ]);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }

    [SetUp]
    public void Setup()
    {
        _scaffold.ClearAssertLogEventsAction();
        _scaffold.ClearLogEvents();
    }

    [TearDown]
    public void TearDown()
    {
        _scaffold.AssertLogEvents();
    }

    [Test]
    public async Task ItShouldPingAnonymously()
    {
        using var factory = new DynamicHttpClientFactory(NullLogger<DynamicHttpClientFactory>.Instance);
        var client = factory.CreateClient(TestIdentities.Samwise.OdinId);
        var uri = $"https://{TestIdentities.Samwise.OdinId}:{WebScaffold.HttpsPort}{UnifiedApiRouteConstants.Health}/ping";
        var response = await client.GetAsync(uri);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task ItShould404OOnUnknownApiRoute()
    {
        using var factory = new DynamicHttpClientFactory(NullLogger<DynamicHttpClientFactory>.Instance);
        var client = factory.CreateClient(TestIdentities.Samwise.OdinId);

        var uri = $"https://{TestIdentities.Samwise.OdinId}:{WebScaffold.HttpsPort}/api/404";
        var response = await client.GetAsync(uri);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        uri = $"https://{TestIdentities.Samwise.OdinId}:{WebScaffold.HttpsPort}/api/v2/404";
        response = await client.GetAsync(uri);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

}
