using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Http;
using Odin.Core.Serialization;
using Odin.Services.Configuration;

#nullable enable

namespace Odin.Services.Email.Relay;

/// <summary>
/// The one authenticated call to SMTP2GO's API (Email:Relay:ApiKey, sent as a header so it never
/// appears in a URL or a body). Tenant onboarding (<see cref="Smtp2GoRelayProvider"/>) and the
/// host's own mail (<see cref="Smtp2GoEmailSender"/>) both go through it.
/// </summary>
public class Smtp2GoApi(OdinConfiguration configuration, IDynamicHttpClientFactory httpClientFactory)
{
    private OdinConfiguration.RelaySection Relay => configuration.Email.Relay;

    public async Task<TResponse> PostAsync<TResponse>(string path, object body, CancellationToken cancellationToken)
    {
        if (!Relay.IsConfigured)
        {
            throw new OdinSystemException("Relay: Email:Relay:Provider is not Smtp2Go");
        }

        var httpClient = httpClientFactory.CreateClient(nameof(Smtp2GoApi));

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Relay.ApiBaseUrl}{path}");
        request.Headers.Add("X-Smtp2go-Api-Key", Relay.ApiKey);
        request.Content = new StringContent(OdinSystemSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var response = await httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // Their errors arrive as a 4xx with the detail in data.error. Surface that rather
            // than the status code alone, which on its own says nothing useful.
            var detail = TryReadError(content) ?? content;
            throw new MailRelayException(
                $"Relay: POST {path} returned {(int)response.StatusCode}: {detail}", (int)response.StatusCode);
        }

        return OdinSystemSerializer.Deserialize<TResponse>(content)
               ?? throw new OdinSystemException($"Relay: POST {path} returned unparseable content");
    }

    private static string? TryReadError(string content)
    {
        try
        {
            // Every endpoint reports a refusal in this shape, whatever it returns on success
            return OdinSystemSerializer.Deserialize<Smtp2GoDomainResponse>(content)?.Data.Error;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
