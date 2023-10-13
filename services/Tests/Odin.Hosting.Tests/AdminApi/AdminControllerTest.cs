using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Core.Services.Admin.Tenants;

namespace Odin.Hosting.Tests.AdminApi;

public class AdminControllerTest
{
    private WebScaffold _scaffold = null!;

    [SetUp]
    public void Init()
    {
        var folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        _scaffold = new WebScaffold(folder);
        var env = new Dictionary<string, string>
        {
            { "Admin__ApiEnabled", "true" },
            { "Admin__ApiKey", "your-secret-api-key-here" },
            { "Admin__ApiKeyHttpHeaderName", "Odin-Admin-Api-Key" },
            { "Admin__ApiPort", "4444" },
            { "Admin__Domain", "admin.dotyou.cloud" },
        };
        _scaffold.RunBeforeAnyTests(envOverrides: env);
    }

    //

    [TearDown]
    public void Cleanup()
    {
        _scaffold.RunAfterAnyTests();
    }

    //

    private static HttpRequestMessage NewRequestMessage(HttpMethod method, string uri)
    {
        return new HttpRequestMessage(method, uri)
        {
            Headers = { { "Odin-Admin-Api-Key", "your-secret-api-key-here" } }
        };
    }

    //

    [Test]
    public async Task ItShouldGetAllTenants()
    {
        var apiClient = WebScaffold.CreateDefaultHttpClient();
        var request = NewRequestMessage(HttpMethod.Get, "https://admin.dotyou.cloud:4444/api/admin/v1/tenants");
        var response = await apiClient.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var tenants = OdinSystemSerializer.Deserialize<List<TenantModel>>(await response.Content.ReadAsStringAsync());
        Assert.That(tenants.Count, Is.GreaterThan(1));
        Assert.That(tenants, Has.Some.Matches<TenantModel>(t => t.Domain == "frodo.dotyou.cloud"));
    }

    //

    [Test]
    public async Task ItShouldGetSpecificTenant()
    {
        var apiClient = WebScaffold.CreateDefaultHttpClient();
        var request = NewRequestMessage(HttpMethod.Get, "https://admin.dotyou.cloud:4444/api/admin/v1/tenants/frodo.dotyou.cloud");
        var response = await apiClient.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var tenant = OdinSystemSerializer.Deserialize<TenantModel>(await response.Content.ReadAsStringAsync());
        Assert.That(tenant.Domain, Is.EqualTo("frodo.dotyou.cloud"));
    }

    //

    [Test]
    public async Task ItShouldEnableAndDisableATenant()
    {
        var apiClient = WebScaffold.CreateDefaultHttpClient();

        // Verify enabled
        {
            var request = NewRequestMessage(HttpMethod.Get, "https://frodo.dotyou.cloud/api/owner/v1/authentication/verifyToken");
            var response = await apiClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        // Enable
        {
            var request = NewRequestMessage(HttpMethod.Put, "https://admin.dotyou.cloud:4444/api/admin/v1/tenants/frodo.dotyou.cloud/enable");
            var response = await apiClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        // Verify still enabled
        {
            var request = NewRequestMessage(HttpMethod.Get, "https://frodo.dotyou.cloud/api/owner/v1/authentication/verifyToken");
            var response = await apiClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        // Disable
        {
            var request = NewRequestMessage(HttpMethod.Put, "https://admin.dotyou.cloud:4444/api/admin/v1/tenants/frodo.dotyou.cloud/disable");
            var response = await apiClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        // Verify disabled
        {
            var request = NewRequestMessage(HttpMethod.Get, "https://frodo.dotyou.cloud/api/owner/v1/authentication/verifyToken");
            var response = await apiClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        }

        // Enable
        {
            var request = NewRequestMessage(HttpMethod.Put, "https://admin.dotyou.cloud:4444/api/admin/v1/tenants/frodo.dotyou.cloud/enable");
            var response = await apiClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        // Verify enabled
        {
            var request = NewRequestMessage(HttpMethod.Get, "https://frodo.dotyou.cloud/api/owner/v1/authentication/verifyToken");
            var response = await apiClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
    }



}