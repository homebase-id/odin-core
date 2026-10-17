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

    private readonly IDynamicHttpClientFactory _httpClientFactory;
    private readonly ILogger<InvitationCodeServiceClient> _logger;
    private readonly Uri? _baseUri;
    private readonly string _apiKey;

    public InvitationCodeServiceClient(
        OdinConfiguration configuration,
        IDynamicHttpClientFactory httpClientFactory,
        ILogger<InvitationCodeServiceClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _apiKey = configuration.Registry.InvitationCodeServiceApiKey;

        var url = configuration.Registry.InvitationCodeServiceUrl;
        if (url == "")
        {
            return;
        }

        if (_apiKey == "")
        {
            logger.LogError("Invitation codes are OFF: Registry:InvitationCodeService:Url is set but :ApiKey is not");
        }
        else if (!TryParseSafeUrl(url, out var baseUri))
        {
            logger.LogError("Invitation codes are OFF: Registry:InvitationCodeService:Url must be https (or loopback), " +
                            "so the key is never sent in clear: {url}", url);
        }
        else
        {
            _baseUri = baseUri;
        }
    }

    public bool IsEnabled => _baseUri != null;

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
    /// Whether the code exists and has a use left. False, not an error, when the service fails: signup then
    /// refuses the code, which is the safe answer.
    /// </summary>
    public async Task<bool> IsValidAsync(string code)
    {
        return (await TryPostAsync<ValidateResponse>("validate", code))?.Valid == true;
    }

    /// <summary>
    /// Takes one use of the code and returns what it carries; null if it has none left or does not exist,
    /// and null too when the service fails, so signup refuses the code as invalid.
    /// </summary>
    public async Task<InvitationCodeUse?> TryUseAsync(string code)
    {
        var response = await TryPostAsync<UseResponse>("use", code);
        return response is { Used: true } ? response.Code : null;
    }

    /// <summary>
    /// Gives back the code's latest use, for a signup that failed after taking it. A failure here only leaks
    /// that use, so it is logged rather than thrown.
    /// </summary>
    public async Task TryUndoUseAsync(string code)
    {
        await TryPostAsync<object>("undo-use", code, readAnswer: false);
    }

    //

    // Every call goes through here: input that cannot be a code is refused without a call, and a failing
    // service is logged as an error and answered with null
    private async Task<T?> TryPostAsync<T>(string path, string code, bool readAnswer = true) where T : class
    {
        if (!IsEnabled || !LooksLikeCode(code))
        {
            return null;
        }

        try
        {
            using var response = await SendAsync(path, code);
            return readAnswer ? await ReadAsync<T>(response) : null;
        }
        catch (Exception e)
        {
            // Any failure, not only the expected ones: a signup must never fail, or lose its cleanup, because
            // the service misbehaved
            _logger.LogError(e, "Invitation-code {path} failed; the invitation-code service is enabled but failing", path);
            return null;
        }
    }

    //

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
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
        var client = _httpClientFactory.CreateClient($"{nameof(InvitationCodeServiceClient)}:{_baseUri!.Host}");
        client.BaseAddress = _baseUri;
        client.Timeout = TimeSpan.FromSeconds(10);

        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{Root}/{path}");
            request.Headers.Add(ApiKeyHeader, _apiKey);
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

    // https, or http to loopback, so the key is never sent in clear. The base address ends in / so the
    // relative request paths keep any path the URL has.
    private static bool TryParseSafeUrl(string url, out Uri? baseUri)
    {
        baseUri = null;
        if (!Uri.TryCreate(url + "/", UriKind.Absolute, out var uri) ||
            !(uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
        {
            return false;
        }

        baseUri = uri;
        return true;
    }
}
