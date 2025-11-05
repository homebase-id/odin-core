using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Odin.Core.Tasks;

namespace Odin.Test.Helpers.WebServers;

public class SimpleWebServer
{
    private readonly WebApplication _app;
    public string PingUrl => $"{_app.Urls.First()}/ping";

    //

    public SimpleWebServer()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0"); // :0 = random port
        _app = builder.Build();
        _app.MapGet("/ping", () => "pong");
        _app.StartAsync().BlockingWait();
    }

    //

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}