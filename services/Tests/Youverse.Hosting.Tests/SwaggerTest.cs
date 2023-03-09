using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Youverse.Hosting.Tests;

public class SwaggerTest
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(initializeIdentity: true);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }

    [Test]
    public async Task TestSwaggerIsUp()
    {
        using var client = _scaffold.CreateAnonymousApiHttpClient(TestIdentities.Samwise.OdinId);
        var result = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.AreEqual(HttpStatusCode.OK, result.StatusCode);
    }    
}