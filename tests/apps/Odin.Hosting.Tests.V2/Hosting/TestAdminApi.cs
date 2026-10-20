#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Odin.Core.Serialization;
using Odin.Services.Admin.Tenants;

namespace Odin.Hosting.Tests.V2.Hosting;

/// <summary>
/// The admin API under TestServer, for fixtures that call it: its config and a client onto it.
/// </summary>
public static class TestAdminApi
{
    public const string Domain = "admin.dotyou.cloud";
    public const int Port = 4444;
    public const string ApiKey = "your-secret-api-key-here";
    public const string ApiKeyHeaderName = "Odin-Admin-Api-Key";

    public static Dictionary<string, string?> Config() => new()
    {
        ["Admin:ApiEnabled"] = "true",
        ["Admin:ApiKey"] = ApiKey,
        ["Admin:ApiKeyHttpHeaderName"] = ApiKeyHeaderName,
        ["Admin:ApiPort"] = Port.ToString(),
        ["Admin:Domain"] = Domain,
    };

    /// <summary>
    /// <c>BaseAddress</c> names the admin domain so <c>Startup</c>'s <c>MapWhen</c> takes it, and the handler stamps
    /// <c>ConnectionInfo.LocalPort</c> because <c>AdminApiRestrictedAttribute</c> checks it and TestServer leaves it at 0.
    /// </summary>
    public static HttpClient CreateClient(OdinHost host) =>
        new(host.Server.CreateHandler(context => context.Connection.LocalPort = Port))
        {
            BaseAddress = new Uri($"https://{Domain}:{Port}/")
        };

    /// <summary>A request to <c>api/admin/v1/{relativePath}</c>, relative to <see cref="CreateClient"/>'s origin.</summary>
    public static HttpRequestMessage Request(HttpMethod method, string relativePath) =>
        new(method, $"api/admin/v1/{relativePath}") { Headers = { { ApiKeyHeaderName, ApiKey } } };

    /// <summary>PATCH tenants/{domain}/plan: the raw response, for tests that expect a refusal.</summary>
    public static async Task<HttpResponseMessage> SetPlanAsync(OdinHost host, string domain, string planId)
    {
        using var client = CreateClient(host);
        var request = Request(HttpMethod.Patch, $"tenants/{domain}/plan");
        request.Content = new StringContent(OdinSystemSerializer.Serialize(new SetTenantPlanRequest { PlanId = planId }),
            Encoding.UTF8, "application/json");
        return await client.SendAsync(request);
    }
}
