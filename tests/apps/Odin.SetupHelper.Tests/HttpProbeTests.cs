using Microsoft.Extensions.DependencyInjection;
using Odin.Core.Cache;

namespace Odin.SetupHelper.Tests;

public class HttpProbeTests
{
    private ServiceProvider _serviceProvider;
    
    [OneTimeSetUp]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IGenericMemoryCache, GenericMemoryCache>();
        services.AddSingleton<HttpProbe>();
        services.AddHttpClient("NoRedirectClient", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(2);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                UseCookies = false,
                AllowAutoRedirect = false
            });
        
        _serviceProvider = services.BuildServiceProvider();
    }    
   
    [OneTimeTearDown]
    public void Dispose()
    {
        _serviceProvider.Dispose();
    }
    
    [Test]
    public async Task ItShouldProbePortHttpPort()
    {
        var httpProbe = _serviceProvider.GetRequiredService<HttpProbe>();
        var (success, message) = await httpProbe.ProbeAsync("http", "id.homebase.id", "80");
        Assert.That(message, Is.EqualTo("Successfully probed http://id.homebase.id:80"));
        Assert.That(success, Is.True);
    }
    
    [Test]
    public async Task ItShouldProbePortHttpsPort()
    {
        var httpProbe = _serviceProvider.GetRequiredService<HttpProbe>();
        var (success, message) = await httpProbe.ProbeAsync("https", "id.homebase.id", "443");
        Assert.That(message, Is.EqualTo("Successfully probed https://id.homebase.id:443"));
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