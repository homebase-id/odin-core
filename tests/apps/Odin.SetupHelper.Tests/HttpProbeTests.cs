using Microsoft.Extensions.DependencyInjection;
using Odin.Core.Cache;
using Odin.Core.Http;

namespace Odin.SetupHelper.Tests;

public class HttpProbeTests
{
    private ServiceProvider _serviceProvider;
    
    [OneTimeSetUp]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IGenericMemoryCache, GenericMemoryCache>();
        services.AddSingleton<HttpProbe>();
        services.AddSingleton<IDynamicHttpClientFactory, DynamicHttpClientFactory>();
        _serviceProvider = services.BuildServiceProvider();
    }    
   
    [OneTimeTearDown]
    public void Dispose()
    {
        _serviceProvider.Dispose();
    }
    
    [Test]
    [Explicit] // Not always available
    public async Task ItShouldProbePortHttpPort()
    {
        var correlationId = Guid.NewGuid();
        var httpProbe = _serviceProvider.GetRequiredService<HttpProbe>();
        var (success, message) = await httpProbe.ProbeAsync("http", "id.homebase.id", "80");
        Assert.That(
            message,
            Is.EqualTo("Successfully probed http://id.homebase.id:80"),
            $"Http probe failed with correlationId: {correlationId}");
        Assert.That(success, Is.True);
    }
    
    [Test]
    [Explicit] // Not always available
    public async Task ItShouldProbePortHttpsPort()
    {
        var correlationId = Guid.NewGuid();
        var httpProbe = _serviceProvider.GetRequiredService<HttpProbe>();
        var (success, message) = await httpProbe.ProbeAsync("https", "id.homebase.id", "443", correlationId);
        Assert.That(
            message,
            Is.EqualTo("Successfully probed https://id.homebase.id:443"),
            $"HTTPS probe failed with correlationId: {correlationId}");
        Assert.That(success, Is.True);
    }

    [Test]
    public async Task ItShouldErrorIfProbePageNotFound()
    {
        var httpProbe = _serviceProvider.GetRequiredService<HttpProbe>();
        var (success, message) = await httpProbe.ProbeAsync("http", "www.google.com", "80");
        Assert.That(message, Is.EqualTo("Failed to probe http://www.google.com:80, www.google.com says: Not Found"));
        Assert.That(success, Is.False);
    }

}