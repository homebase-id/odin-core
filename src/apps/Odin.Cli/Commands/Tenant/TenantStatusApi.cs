using System.Net;
using System.Text;
using Odin.Core.Serialization;
using Odin.Services.Admin.Tenants;
using Odin.Services.Registry;
using Spectre.Console;
using Odin.Cli.Commands.Base;

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

    public static async Task<TenantStatusState> SetStatusAsync(HttpClient httpClient, string domain, TenantStatus status,
        DisabledReason? reason = null)
    {
        var body = OdinSystemSerializer.Serialize(new SetTenantStatusRequest { Status = status, DisabledReason = reason });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await httpClient.PatchAsync($"tenants/{domain}/status", content);
        await EnsureSuccessAsync(response, domain);
        var json = await response.Content.ReadAsStringAsync();
        return OdinSystemSerializer.Deserialize<TenantStatusState>(json) ??
               throw new Exception($"{response.RequestMessage?.RequestUri}: empty response");
    }

    //

    public static async Task<TenantStatusState> UnlockMovedAsync(HttpClient httpClient, string domain)
    {
        var response = await httpClient.PostAsync($"tenants/{domain}/unlock-moved", null);
        await EnsureSuccessAsync(response, domain);
        return OdinSystemSerializer.Deserialize<TenantStatusState>(await response.Content.ReadAsStringAsync()) ??
               throw new Exception($"{response.RequestMessage?.RequestUri}: empty response");
    }

    //

    public static void WriteChange(string domain, TenantStatusState previous, TenantStatus status, DisabledReason? reason)
    {
        AnsiConsole.MarkupLineInterpolated($"{domain}: {Describe(status, reason)} (was {Describe(previous.Status, previous.DisabledReason)})");
    }

    //

    public static string Describe(TenantStatus status, DisabledReason? reason)
    {
        return reason.HasValue ? $"{status} ({reason})" : status.ToString();
    }

    //

    public static async Task EnsureSuccessAsync(HttpResponseMessage response, string domain,
        HttpStatusCode expected = HttpStatusCode.OK)
    {
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new Exception($"Tenant {domain} was not found");
        }
        await ApiResponse.EnsureAsync(response, expected);
    }
}
