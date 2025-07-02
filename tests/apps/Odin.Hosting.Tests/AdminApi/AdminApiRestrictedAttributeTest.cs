using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Odin.Hosting.Tests.AdminApi;

public class AdminApiRestrictedAttributeTest
{
    private WebScaffold _scaffold = null!;

    [SetUp]
    public void Init()
    {
        var folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        _scaffold = new WebScaffold(folder);
    }

    //

    [TearDown]
    public void Cleanup()
    {
        _scaffold.RunAfterAnyTests();
    }

    //

    [Test]
    public void PingShouldRefuseConnectionIfNotEnabled()
    {
        var env = new Dictionary<string, string>
        {
            { "Admin__ApiEnabled", "false" },
            { "Admin__Domain", "admin.dotyou.cloud" },
        };
        _scaffold.RunBeforeAnyTests(envOverrides: env);

        var apiClient = WebScaffold.HttpClientFactory.CreateClient($"admin.dotyou.cloud:{WebScaffold.AdminPort}");
        var exception = Assert.ThrowsAsync<HttpRequestException>(() =>
            apiClient.GetAsync($"https://admin.dotyou.cloud:{WebScaffold.AdminPort}/api/admin/v1/ping"));
        Assert.That(exception.Message, Contains.Substring("refused"));
    }

    //

    [Test]
    public async Task PingShouldReturn404IfWrongPort()
    {
        var env = new Dictionary<string, string>
        {
            { "Admin__ApiEnabled", "true" },
            { "Admin__ApiPort", "4444" },
            { "Admin__Domain", "admin.dotyou.cloud" },
        };
        _scaffold.RunBeforeAnyTests(envOverrides: env);

        var apiClient = WebScaffold.HttpClientFactory.CreateClient($"admin.dotyou.cloud:{WebScaffold.AdminPort}");
        var response = await apiClient.GetAsync($"https://admin.dotyou.cloud:{WebScaffold.HttpsPort}/api/admin/v1/ping");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    //

    [Test]
    public async Task PingShouldReturn404IfWrongDomain()
    {
        var env = new Dictionary<string, string>
        {
            { "Admin__ApiEnabled", "true" },
            { "Admin__ApiPort", "4444" },
            { "Admin__Domain", "admin.dotyou.cloud" },
        };
        _scaffold.RunBeforeAnyTests(envOverrides: env);

        var apiClient = WebScaffold.HttpClientFactory.CreateClient("frodo.dotyou.cloud:4444");
        var response = await apiClient.GetAsync($"https://frodo.dotyou.cloud:4444/api/admin/v1/ping");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    //

    [Test]
    public async Task PingShouldReturn401IfWrongApiKey()
    {
        var env = new Dictionary<string, string>
        {
            { "Admin__ApiEnabled", "true" },
            { "Admin__ApiKey", "your-secret-api-key-here" },
            { "Admin__ApiKeyHttpHeaderName", "Odin-Admin-Api-Key" },
            { "Admin__ApiPort", "4444" },
            { "Admin__Domain", "admin.dotyou.cloud" },
        };
        _scaffold.RunBeforeAnyTests(envOverrides: env);

        var apiClient = WebScaffold.HttpClientFactory.CreateClient("admin.dotyou.cloud:4444");
        var request = new HttpRequestMessage(HttpMethod.Get, "https://admin.dotyou.cloud:4444/api/admin/v1/ping")
        {
            Headers = { { "Odin-Admin-Api-Key", "WRONG-KEY" } },
        };
        var response = await apiClient.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    //

    [Test]
    public async Task PingShouldReturn200WhenAllIsGood()
    {
        var env = new Dictionary<string, string>
        {
            { "Admin__ApiEnabled", "true" },
            { "Admin__ApiKey", "your-secret-api-key-here" },
            { "Admin__ApiKeyHttpHeaderName", "Odin-Admin-Api-Key" },
            { "Admin__ApiPort", "4444" },
            { "Admin__Domain", "admin.dotyou.cloud" },
        };
        _scaffold.RunBeforeAnyTests(envOverrides: env);

        var apiClient = WebScaffold.HttpClientFactory.CreateClient("admin.dotyou.cloud:4444");
        var request = new HttpRequestMessage(HttpMethod.Get, "https://admin.dotyou.cloud:4444/api/admin/v1/ping")
        {
            Headers = { { "Odin-Admin-Api-Key", "your-secret-api-key-here" } },
        };
        var response = await apiClient.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("pong"));
    }

    //

}