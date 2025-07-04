using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Services.Admin.Tenants;
using Odin.Services.Fingering;

namespace Odin.Hosting.Tests.Fingering;

public class WebfingerControllerTests
{
    private WebScaffold _scaffold = null!;

    [SetUp]
    public void Init()
    {
        var folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests();
    }

    //

    [TearDown]
    public void Cleanup()
    {
        _scaffold.RunAfterAnyTests();
    }

    //

    [Test]
    public async Task ItShouldGetWebfinger()
    {
        var apiClient = WebScaffold.HttpClientFactory.CreateClient("frodo.dotyou.cloud:4444");
        var request = new HttpRequestMessage(HttpMethod.Get, "https://frodo.dotyou.cloud:4444/.well-known/webfinger");
        var response = await apiClient.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var result = OdinSystemSerializer.Deserialize<WebFingerResponse>(await response.Content.ReadAsStringAsync());

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Subject, Is.EqualTo("acct:@frodo.dotyou.cloud"));
    }

}