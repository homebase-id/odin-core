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
/// code, uses it, and gives the use back if registering fails. On only when Registry:InvitationCodeService:Url and
/// :ApiKey are both set and the URL is https (or loopback); anything else is off, with an error logged when this is created.
/// </summary>
public class InvitationCodeServiceClient
{
    private const string ApiKeyHeader = "Odin-Api-Key";
    private const string Root = "api/v1/invitation-codes";

    // A code is 12 Crockford base32 characters; the service also reads O as 0 and I, L as 1, and ignores
    // dashes and spaces. Anything else cannot be a code, and is refused without calling the service.
    private const int CodeLength = 12;
    private const int MaxInputLength = 32;
    private const string CodeCharacters = "0123456789ABCDEFGHIJKLMNOPQRSTVWXYZ";

    private record CodeRequest(string Code);
    private record ValidateResponse(bool Valid);
    private record UseResponse(bool Used, InvitationCodeUse? Code);

    private readonly OdinConfiguration _configuration;
    private readonly IDynamicHttpClientFactory _httpClientFactory;
    private readonly ILogger<InvitationCodeServiceClient> _logger;

    public InvitationCodeServiceClient(
        OdinConfiguration configuration,
        IDynamicHttpClientFactory httpClientFactory,
        ILogger<InvitationCodeServiceClient> logger)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        var url = configuration.Registry.InvitationCodeServiceUrl;
        if (url == "")
        {
            return;
        }

        if (configuration.Registry.InvitationCodeServiceApiKey == "")
        {
            logger.LogError("Invitation codes are OFF: Registry:InvitationCodeService:Url is set but :ApiKey is not");
        }
        else if (!IsSafeUrl(url))
        {
            logger.LogError("Invitation codes are OFF: Registry:InvitationCodeService:Url must be https (or loopback), " +
                            "so the key is never sent in clear: {url}", url);
        }
        else
        {
            IsEnabled = true;
        }
    }

    public bool IsEnabled { get; }

    /// <summary>
    /// Whether the input has the shape of a code at all. Checked before every call, so garbage costs nothing.
    /// </summary>
    public static bool LooksLikeCode(string? input)
    {
        if (string.IsNullOrEmpty(input) || input.Length > MaxInputLength)
        {
            return false;
        }

        var count = 0;
        foreach (var c in input.ToUpperInvariant())
        {
            if (c is '-' or ' ')
            {
                continue;
            }
            if (!CodeCharacters.Contains(c))
            {
                return false;
            }
            count++;
        }

        return count == CodeLength;
    }

    /// <summary>
    /// Whether the code exists and has a use left. False, not an error, when the service is unreachable:
    /// signup then refuses the code, which is the safe answer.
    /// </summary>
    public async Task<bool> IsValidAsync(string code)
    {
        if (!LooksLikeCode(code))
        {
            return false;
        }

        try
        {
            return (await PostAsync<ValidateResponse>("validate", code)).Valid;
        }
        catch (InvitationCodeServiceException e)
        {
            _logger.LogError(e, "Could not validate an invitation code; the invitation-code service is enabled but failing");
            return false;
        }
    }

    /// <summary>
    /// Takes one use of the code and returns what it carries; null if it has none left or does not exist.
    /// Null too, logged as an error, when the service is unreachable: signup then refuses the code as invalid.
    /// </summary>
    public async Task<InvitationCodeUse?> TryUseAsync(string code)
    {
        if (!LooksLikeCode(code))
        {
            return null;
        }

        try
        {
            var response = await PostAsync<UseResponse>("use", code);
            return response.Used ? response.Code : null;
        }
        catch (InvitationCodeServiceException e)
        {
            _logger.LogError(e, "Could not use an invitation code; the invitation-code service is enabled but failing");
            return null;
        }
    }

    public async Task UndoUseAsync(string code)
    {
        using var _ = await SendAsync("undo-use", code);
    }

    //

    private async Task<T> PostAsync<T>(string path, string code)
    {
        using var response = await SendAsync(path, code);
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

        var baseUri = new Uri(_configuration.Registry.InvitationCodeServiceUrl + "/");
        var client = _httpClientFactory.CreateClient($"{nameof(InvitationCodeServiceClient)}:{baseUri.Host}");
        client.BaseAddress = baseUri;
        client.Timeout = TimeSpan.FromSeconds(10);

        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{Root}/{path}");
            request.Headers.Add(ApiKeyHeader, _configuration.Registry.InvitationCodeServiceApiKey);
            request.Content = JsonContent.Create(new CodeRequest(code));
            response = await client.SendAsync(request);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            throw new InvitationCodeServiceException($"The invitation-code service is unreachable: {e.Message}", e);
        }

        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            throw new InvitationCodeServiceException(
                $"The invitation-code service answered {(int)response.StatusCode} to {path}");
        }

        return response;
    }

    //

    private static bool IsSafeUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));
    }
}
