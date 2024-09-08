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
    async (string domainName, string hostPort, IGenericMemoryCache cache) =>
        await TcpProbe(domainName, hostPort, cache));

app.MapGet("/api/v1/probe-http/{domainName}/{hostPort}",
    async (string domainName, string hostPort, IHttpClientFactory httpClientFactory, IGenericMemoryCache cache) =>
        await HttpProbe("http", domainName, hostPort, httpClientFactory, cache));

app.MapGet("/api/v1/probe-https/{domainName}/{hostPort}",
    async (string domainName, string hostPort, IHttpClientFactory httpClientFactory, IGenericMemoryCache cache) =>
        await HttpProbe("https", domainName, hostPort, httpClientFactory, cache));

app.MapGet("/api/v1/resolve-ip4/{domainName}",
    async (string domainName, IGenericMemoryCache cache, IAuthoritativeDnsLookup authoritativeDnsLookup, ILookupClient lookupClient) =>
        await ResolveIp(domainName, cache, authoritativeDnsLookup, lookupClient));


app.Run();

////////////////////////////////////////////////////////////////////////

async Task<IResult> TcpProbe(
    string domainName,
    string hostPort,
    IGenericMemoryCache cache)
{
    domainName = domainName.ToLower();
    if (!AsciiDomainNameValidator.TryValidateDomain(domainName))
    {
        return Results.BadRequest("Invalid domain name");
    }

    if (!int.TryParse(hostPort, out var port))
    {
        return Results.BadRequest("Invalid port number");
    }

    if (port is < 1 or > 65535)
    {
        return Results.BadRequest("Port number out of range");
    }

    var cacheKey = $"tcp:{domainName}:{port}";
    if (cache.TryGet<RequestResult>(cacheKey, out var requestResult) && requestResult != null)
    {
        return requestResult.Success
            ? Results.Ok($"{requestResult.Message} [cache hit]")
            : Results.BadRequest($"{requestResult.Message} [cache hit]");
    }

    try
    {
        using var tcpClient = new TcpClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await tcpClient.ConnectAsync(domainName, port, cts.Token);
        var result = new RequestResult(true, $"Successfully connected to TCP: {domainName}:{port}");
        cache.Set(cacheKey, result, TimeSpan.FromMinutes(1));
        return Results.Ok(result.Message);
    }
    catch (Exception)
    {
        var result = new RequestResult(false, $"Failed to connect to TCP: {domainName}:{port}");
        cache.Set(cacheKey, result, TimeSpan.FromMinutes(1));
        return Results.BadRequest(result.Message);
    }
}

//

async Task<IResult> HttpProbe(
    string scheme,
    string domainName,
    string hostPort,
    IHttpClientFactory httpClientFactory,
    IGenericMemoryCache cache)
{
    domainName = domainName.ToLower();
    if (!AsciiDomainNameValidator.TryValidateDomain(domainName))
    {
        return Results.BadRequest("Invalid domain name");
    }

    if (!int.TryParse(hostPort, out var port))
    {
        return Results.BadRequest("Invalid port number");
    }

    if (port is < 1 or > 65535)
    {
        return Results.BadRequest("Port number out of range");
    }

    var uri = new Uri($"{scheme}://{domainName}:{port}/.well-known/acme-challenge/ping");
    var cacheKey = uri.ToString();

    if (cache.TryGet<RequestResult>(cacheKey, out var requestResult) && requestResult != null)
    {
        return requestResult.Success
            ? Results.Ok($"{requestResult.Message} [cache hit]")
            : Results.BadRequest($"{requestResult.Message} [cache hit]");
    }

    RequestResult result;
    var client = httpClientFactory.CreateClient("NoRedirectClient");
    try
    {
        var response = await client.GetAsync(uri);
        var body = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            if (body == "pong")
            {
                result = new RequestResult(true, $"Successfully probed {scheme}://{domainName}:{port}");
                cache.Set(cacheKey, result, TimeSpan.FromMinutes(1));
                return Results.Ok(result.Message);
            }
            result = new RequestResult(false, $"Successfully probed {scheme}://{domainName}:{port}, but received unexpected response");
            cache.Set(cacheKey, result, TimeSpan.FromMinutes(1));
            return Results.BadRequest(result.Message);
        }
        result = new RequestResult(false, $"Failed to probe {scheme}://{domainName}:{port}: {response.ReasonPhrase}");
        cache.Set(cacheKey, result, TimeSpan.FromMinutes(1));
        return Results.BadRequest(result.Message);
    }
    catch (HttpRequestException e)
    {
        result = new RequestResult(false, $"Failed to probe {scheme}://{domainName}:{port}: {e.Message}");
        cache.Set(cacheKey, result, TimeSpan.FromSeconds(5));
        return Results.BadRequest(result.Message);
    }
    catch (TaskCanceledException)
    {
        result = new RequestResult(false, $"Failed to probe {scheme}://{domainName}:{port}: time out");
        cache.Set(cacheKey, result, TimeSpan.FromSeconds(5));
        return Results.BadRequest(result.Message);
    }
    catch (Exception)
    {
        result = new RequestResult(false, $"Failed to probe {scheme}://{domainName}:{port}: unknown server error");
        cache.Set(cacheKey, result, TimeSpan.FromSeconds(5));
        return Results.BadRequest(result.Message);
    }
}

//

async Task<IResult> ResolveIp(
    string domainName,
    IGenericMemoryCache cache,
    IAuthoritativeDnsLookup authoritativeDnsLookup,
    ILookupClient lookupClient)
{
    domainName = domainName.ToLower();
    if (!AsciiDomainNameValidator.TryValidateDomain(domainName))
    {
        return Results.BadRequest("Invalid domain name");
    }

    var cacheKey = $"resolve-ip:{domainName}";
    if (cache.TryGet<RequestResult>(cacheKey, out var requestResult) && requestResult != null)
    {
        return requestResult.Success
            ? Results.Ok($"{requestResult.Message} [cache hit]")
            : Results.BadRequest($"{requestResult.Message} [cache hit]");
    }

    RequestResult result;
    var authoritativeResult = await authoritativeDnsLookup.LookupDomainAuthority(domainName);
    if (authoritativeResult.Exception != null)
    {
        result = new RequestResult(false, $"Failed to resolve ip: {authoritativeResult.Exception.Message}");
        cache.Set(cacheKey, result, TimeSpan.FromMinutes(1));
        return Results.BadRequest(result.Message);
    }

    if (authoritativeResult.AuthoritativeNameServer == "")
    {
        result = new RequestResult(false, $"No authoritative name server found for {domainName}");
        cache.Set(cacheKey, result, TimeSpan.FromMinutes(1));
        return Results.BadRequest(result.Message);
    }

    HER!

    return Results.Ok(authoritativeResult.AuthoritativeNameServer);


    // var result = await lookupClient.QueryAsync(domainName, QueryType.A);
    // if (result.HasError)
    // {
    //     return Results.BadRequest(result.ErrorMessage);
    // }
    //
    // var addresses = result.Answers.ARecords().Select(x => x.Address.ToString()).ToList();
    //return Results.Ok(string.Join(", ", addresses));
}

//

public class RequestResult(bool success, string message)
{
    public bool Success { get; } = success;
    public string Message { get; } = message;
}
