using System.Net.Sockets;
using DnsClient;
using Odin.Core.Cache;
using Odin.Core.Dns;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Logging.CorrelationId.Serilog;
using Odin.Core.Util;
using Odin.SetupHelper;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;

var builder = WebApplication.CreateBuilder(args);

//
// Logging
//

const string logOutputTemplate = "{Timestamp:o} {Level:u3} {CorrelationId} {Message:lj}{NewLine}{Exception}";
var logOutputTheme = SystemConsoleTheme.Literate;

builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithCorrelationId(new CorrelationUniqueIdGenerator())
    .WriteTo.Console(outputTemplate: logOutputTemplate, theme: logOutputTheme));

//
// Swagger
//

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

//
// HttpClientFactory
//

builder.Services.AddHttpClient("NoRedirectClient", client =>
{
    client.Timeout = TimeSpan.FromSeconds(2);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    UseCookies = false,
    AllowAutoRedirect = false
});

//
// Misc
//

builder.Services.AddSingleton<ILookupClient, LookupClient>();
builder.Services.AddSingleton<IAuthoritativeDnsLookup, AuthoritativeDnsLookup>();
builder.Services.AddSingleton<ICorrelationIdGenerator, CorrelationUniqueIdGenerator>();
builder.Services.AddSingleton<ICorrelationContext, CorrelationContext>();
builder.Services.AddSingleton<IGenericMemoryCache, GenericMemoryCache>();
builder.Services.AddSingleton<TcpProbe>();
builder.Services.AddSingleton<HttpProbe>();
builder.Services.AddSingleton<DnsProbe>();

var app = builder.Build();

// Swagger
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.MapGet("/ping", () => "pong");

app.MapGet("/api/v1/probe-tcp/{domainName}/{hostPort}", 
    async (string domainName, string hostPort, TcpProbe tcpProbe) =>
    {
        var (success, message) = await tcpProbe.ProbeAsync(domainName, hostPort);
        return success
            ? Results.Ok(message)
            : Results.BadRequest(message);
    });

app.MapGet("/api/v1/probe-http/{domainName}/{hostPort}",
    async (string domainName, string hostPort, HttpProbe httpProbe) =>
    {
        var (success, message) = await httpProbe.ProbeAsync("http", domainName, hostPort);
        return success
            ? Results.Ok(message)
            : Results.BadRequest(message);

    });

app.MapGet("/api/v1/probe-https/{domainName}/{hostPort}",
    async (string domainName, string hostPort, HttpProbe httpProbe) =>
    {
        var (success, message) = await httpProbe.ProbeAsync("https", domainName, hostPort);
        return success
            ? Results.Ok(message)
            : Results.BadRequest(message);
    });

app.MapGet("/api/v1/resolve-ip/{domainName}",
    async (string domainName, DnsProbe dnsProbe) =>
    {
        var (ip, message) = await dnsProbe.ResolveIpAsync(domainName);
        return ip != ""
            ? Results.Ok(ip)
            : Results.BadRequest(message);
    });

app.Run();



