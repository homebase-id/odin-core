using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Odin.Core.Tasks;

namespace Odin.Test.Helpers.WebServers;

public class SimpleWebServer
{
    private readonly WebApplication _app;
    public string BaseUrl => _app.Urls.First();
    public string PingUrl => $"{BaseUrl}/ping";

    //

    /// <param name="map">Further endpoints, mapped before it starts.</param>
    public SimpleWebServer(Action<WebApplication>? map = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0"); // :0 = random port
        _app = builder.Build();
        _app.MapGet("/ping", () => "pong");
        map?.Invoke(_app);
        _app.StartAsync().BlockingWait();
    }

    //

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}