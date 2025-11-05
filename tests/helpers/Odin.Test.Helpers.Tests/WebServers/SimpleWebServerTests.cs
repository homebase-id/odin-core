using Microsoft.Extensions.Logging;
using Moq;
using Odin.Core.Http;
using Odin.Test.Helpers.WebServers;

namespace Odin.Test.Helpers.Tests.WebServers;

public class SimpleWebServerTests
{
    [Test]
    public async Task ItShouldStartAndStop()
    {
        var logger = new Mock<ILogger<DynamicHttpClientFactory>>();
        await using var simpleWebServer = new SimpleWebServer();

        using var factory = new DynamicHttpClientFactory(
            logger: logger.Object,
            defaultHandlerLifetime: TimeSpan.FromMilliseconds(100),
            cleanupInterval: TimeSpan.FromMilliseconds(20),
            disposeGracePeriod: TimeSpan.FromMilliseconds(1000));

        var client = factory.CreateClient("alskdaldasdlkj");

        var response = await client.GetAsync(simpleWebServer.PingUrl);
        Assert.That(response.IsSuccessStatusCode, Is.True);
    }
}