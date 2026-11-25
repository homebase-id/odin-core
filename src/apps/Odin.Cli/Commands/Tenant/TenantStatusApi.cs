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
    public static async Task<TenantStatusState> SetStatusAsync(HttpClient httpClient, string domain, TenantStatus status,
        DisabledReason? reason = null)
    {
        var body = OdinSystemSerializer.Serialize(new SetTenantStatusRequest { Status = status, DisabledReason = reason });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        return await ReadStateAsync(await httpClient.PatchAsync($"tenants/{domain}/status", content), domain);
    }

    //

    public static async Task<SetTenantPlanResponse> SetPlanAsync(HttpClient httpClient, string domain, string planId,
        long? quotaBytes = null, bool? publicWebPresence = null)
    {
        var body = OdinSystemSerializer.Serialize(new SetTenantPlanRequest
            { PlanId = planId, QuotaBytes = quotaBytes, PublicWebPresence = publicWebPresence });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await httpClient.PatchAsync($"tenants/{domain}/plan", content);
        await EnsureSuccessAsync(response, domain);
        return OdinSystemSerializer.Deserialize<SetTenantPlanResponse>(await response.Content.ReadAsStringAsync()) ??
               throw new Exception($"{response.RequestMessage?.RequestUri}: empty response");
    }

    //

    public static async Task<TenantStatusState> UnlockMovedAsync(HttpClient httpClient, string domain) =>
        await ReadStateAsync(await httpClient.PostAsync($"tenants/{domain}/unlock-moved", null), domain);

    //

    public static async Task<TenantStatusState> ResumeAsync(HttpClient httpClient, string domain) =>
        await ReadStateAsync(await httpClient.PostAsync($"tenants/{domain}/resume", null), domain);

    /// <summary>The previous status every status change answers with</summary>
    private static async Task<TenantStatusState> ReadStateAsync(HttpResponseMessage response, string domain)
    {
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
