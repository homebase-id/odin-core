using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Odin.Hosting.Tests.Registration;

public class RegistrationRestrictedAttributeTest
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
    public void ItShouldShouldFailToConnectIfNotEnabled()
    {
        var env = new Dictionary<string, string>
        {
            { "Registry__ProvisioningEnabled", "false" }
        };
        _scaffold.RunBeforeAnyTests(envOverrides: env);

        var apiClient = WebScaffold.CreateDefaultHttpClient();
        var exception = Assert.ThrowsAsync<HttpRequestException>(async () =>
            await apiClient.GetAsync($"https://provisioning.dotyou.cloud:{WebScaffold.HttpsPort}/api/registration/v1/registration/is-valid-domain/example.com"));
        Assert.That(exception!.Message, Is.EqualTo("The SSL connection could not be established, see inner exception."));
    }

}