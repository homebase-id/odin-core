using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Http;
using Odin.Services.Configuration;

namespace Odin.Services.Registry.Registration;

#nullable enable

/// <summary>
/// A code the invitation-code service let a signup use, with what it carries.
/// </summary>
public record InvitationCodeUse(string Code, string? Owner, string Source, bool EnablePublicWebPresence);

/// <summary>
/// The invitation-code service is unreachable or answered with an error.
/// </summary>
public class InvitationCodeServiceException(string message, Exception? inner = null) : OdinSystemException(message, inner);

/// <summary>
/// odin-core's one plug point into the invitation-code service, homebase-id/homebase-invites (#1908): signup checks a
/// code, uses it, and gives the use back if registering fails. Off unless Registry:InvitationCodeService:Url
/// is set.
/// </summary>
public class InvitationCodeServiceClient(
    OdinConfiguration configuration,
    IDynamicHttpClientFactory httpClientFactory,
    ILogger<InvitationCodeServiceClient> logger)
{
    private const string ApiKeyHeader = "Odin-Api-Key";
    private const string Root = "api/v1/invitation-codes";

    private record CodeRequest(string Code);
    private record ValidateResponse(bool Valid);
    private record UseResponse(bool Used, InvitationCodeUse? Code);

    public bool IsEnabled => configuration.Registry.InvitationCodeServiceUrl != "";

    /// <summary>
    /// Whether the code exists and has a use left. False, not an error, when the service is unreachable:
    /// signup then refuses the code, which is the safe answer.
    /// </summary>
    public async Task<bool> IsValidAsync(string code)
    {
        try
        {
            return (await PostAsync<ValidateResponse>("validate", code)).Valid;
        }
        catch (InvitationCodeServiceException e)
        {
            logger.LogError(e, "Could not validate an invitation code; the invitation-code service is enabled but failing");
            return false;
        }
    }

    /// <summary>
    /// Takes one use of the code and returns what it carries; null if it has none left or does not exist.
    /// Null too, logged as an error, when the service is unreachable: signup then refuses the code as invalid.
    /// </summary>
    public async Task<InvitationCodeUse?> TryUseAsync(string code)
    {
        try
        {
            var response = await PostAsync<UseResponse>("use", code);
            return response.Used ? response.Code : null;
        }
        catch (InvitationCodeServiceException e)
        {
            logger.LogError(e, "Could not use an invitation code; the invitation-code service is enabled but failing");
            return null;
        }
    }

    public async Task UndoUseAsync(string code)
    {
        await SendAsync("undo-use", code);
    }

    //

    private async Task<T> PostAsync<T>(string path, string code)
    {
        var response = await SendAsync(path, code);
        try
        {
            return await response.Content.ReadFromJsonAsync<T>()
                   ?? throw new InvitationCodeServiceException("The invitation-code service sent an empty answer");
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or NotSupportedException)
        {
            throw new InvitationCodeServiceException("The invitation-code service sent an unreadable answer", e);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(string path, string code)
    {
        if (!IsEnabled)
        {
            throw new InvitationCodeServiceException("The invitation-code service is not configured");
        }

        var baseUri = new Uri(configuration.Registry.InvitationCodeServiceUrl + "/");
        var client = httpClientFactory.CreateClient($"{nameof(InvitationCodeServiceClient)}:{baseUri.Host}");
        client.BaseAddress = baseUri;
        client.Timeout = TimeSpan.FromSeconds(10);

        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{Root}/{path}");
            request.Headers.Add(ApiKeyHeader, configuration.Registry.InvitationCodeServiceApiKey);
            request.Content = JsonContent.Create(new CodeRequest(code));
            response = await client.SendAsync(request);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            throw new InvitationCodeServiceException($"The invitation-code service is unreachable: {e.Message}", e);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvitationCodeServiceException(
                $"The invitation-code service answered {(int)response.StatusCode} to {path}");
        }

        return response;
    }
}
