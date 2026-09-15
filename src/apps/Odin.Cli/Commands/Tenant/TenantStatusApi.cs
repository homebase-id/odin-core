using System.Net;
using System.Text;
using Odin.Core.Serialization;
using Odin.Services.Admin.Tenants;
using Odin.Services.Registry;
using Spectre.Console;

namespace Odin.Cli.Commands.Tenant;

internal static class TenantStatusApi
{
    public static async Task<TenantModel> GetTenantAsync(HttpClient httpClient, string domain)
    {
        var response = await httpClient.GetAsync($"tenants/{domain}");
        await EnsureSuccessAsync(response, domain);
        var json = await response.Content.ReadAsStringAsync();
        return OdinSystemSerializer.Deserialize<TenantModel>(json) ?? new TenantModel();
    }

    //

    public static async Task<TenantStatusModel> SetStatusAsync(HttpClient httpClient, string domain, TenantStatus status,
        DisabledReason? reason = null)
    {
        var body = OdinSystemSerializer.Serialize(new SetTenantStatusRequest { Status = status, DisabledReason = reason });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await httpClient.PatchAsync($"tenants/{domain}/status", content);
        await EnsureSuccessAsync(response, domain);
        var json = await response.Content.ReadAsStringAsync();
        return OdinSystemSerializer.Deserialize<TenantStatusModel>(json) ?? new TenantStatusModel();
    }

    //

    public static void WriteChange(string domain, TenantStatusModel previous, TenantStatus status, DisabledReason? reason)
    {
        AnsiConsole.MarkupLineInterpolated($"{domain}: {Describe(status, reason)} (was {Describe(previous.Status, previous.DisabledReason)})");
    }

    //

    public static string Describe(TenantStatus status, DisabledReason? reason)
    {
        return reason.HasValue ? $"{status} ({reason})" : status.ToString();
    }

    //

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string domain)
    {
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new Exception($"Tenant {domain} was not found");
        }
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var detail = await response.Content.ReadAsStringAsync();
            throw new Exception($"{response.RequestMessage?.RequestUri}: refused: {detail}");
        }
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new Exception($"{response.RequestMessage?.RequestUri}: " + response.StatusCode);
        }
    }
}
