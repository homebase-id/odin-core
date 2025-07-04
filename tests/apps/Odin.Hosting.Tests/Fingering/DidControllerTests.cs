using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Services.Fingering;

namespace Odin.Hosting.Tests.Fingering;

public class DidControllerTests
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
    public async Task ItShouldGetDidWeb()
    {
        var apiClient = WebScaffold.HttpClientFactory.CreateClient("frodo.dotyou.cloud:4444");
        var request = new HttpRequestMessage(HttpMethod.Get, "https://frodo.dotyou.cloud:4444/.well-known/did.json");
        var response = await apiClient.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var result = OdinSystemSerializer.Deserialize<DidWebResponse>(await response.Content.ReadAsStringAsync());
        
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Id, Is.EqualTo("did:web:frodo.dotyou.cloud"));
        Assert.That(result.VerificationMethod, Is.Not.Empty);
    }

}