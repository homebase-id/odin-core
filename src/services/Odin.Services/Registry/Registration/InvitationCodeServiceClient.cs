using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Http;
using Odin.Services.Configuration;
using Refit;

namespace Odin.Services.Registry.Registration;

#nullable enable

/// <summary>
/// A code the invitation-code service let a signup use, with what it carries.
/// </summary>
/// <param name="PlanId">The hosting plan the code gives the new identity; null for none. Absent from older
/// services, which reads as null.</param>
/// <param name="QuotaBytes">What the plan gives (docs/hosting-plans.md): its quota, stored on the new identity;
/// null leaves it the host's default</param>
/// <param name="PublicWebPresence">What the plan gives: a public home page or not; null leaves it as signup
/// would without a plan</param>
/// <remarks>The service may still send the code's older enablePublicWebPresence; it is ignored.</remarks>
public record InvitationCodeUse(string Code, string? Owner, string Source, string? PlanId = null, long? QuotaBytes = null,
    bool? PublicWebPresence = null)
{
    public RegistrationInvitation ToRegistrationInvitation() => new(Code, Owner, Source);
}

internal record InvitationCodeRequest(string Code);
internal record InvitationCodeValidateResponse(bool Valid);
internal record InvitationCodeUseResponse(bool Used, InvitationCodeUse? Code);

/// <summary>
/// The invitation-code service's API, homebase-id/homebase-invites; its README has the details.
/// </summary>
internal interface IInvitationCodeApi
{
    [Post("/api/v1/invitation-codes/validate")]
    Task<InvitationCodeValidateResponse> Validate([Body] InvitationCodeRequest request);

    [Post("/api/v1/invitation-codes/use")]
    Task<InvitationCodeUseResponse> Use([Body] InvitationCodeRequest request);

    [Post("/api/v1/invitation-codes/undo-use")]
    Task UndoUse([Body] InvitationCodeRequest request);
}

/// <summary>
/// odin-core's one plug point into the invitation-code service, homebase-id/homebase-invites (#1908): signup checks a
/// code, uses it, and gives the use back if registering fails. On only when Registry:InvitationCodeService:Url and
/// :ApiKey are both set and the URL is https (or loopback); anything else is off, with an error logged when this is created.
/// </summary>
public class InvitationCodeServiceClient
{
    private const string ApiKeyHeader = "Odin-Api-Key";

    // A code is 12 Crockford base32 characters; the service also reads O as 0 and I, L as 1, and ignores
    // dashes and spaces. Anything else cannot be a code, and is refused without calling the service.
    private const int CodeLength = 12;
    private const int MaxInputLength = 32;
    private const string CodeCharacters = "0123456789ABCDEFGHIJKLMNOPQRSTVWXYZ";

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
        return (await TryCallAsync("validate", code, (api, request) => api.Validate(request)))?.Valid == true;
    }

    /// <summary>
    /// Takes one use of the code and returns what it carries; null if it has none left or does not exist,
    /// and null too when the service fails, so signup refuses the code as invalid.
    /// </summary>
    public async Task<InvitationCodeUse?> TryUseAsync(string code)
    {
        var response = await TryCallAsync("use", code, (api, request) => api.Use(request));
        return response is { Used: true } ? response.Code : null;
    }

    /// <summary>
    /// Gives back the code's latest use, for a signup that failed after taking it. A failure here only leaks
    /// that use, so it is logged rather than thrown.
    /// </summary>
    public Task TryUndoUseAsync(string code)
    {
        return TryCallAsync("undo-use", code, async (api, request) =>
        {
            await api.UndoUse(request);
            return true;
        });
    }

    //

    // Every call goes through here: input that cannot be a code is refused without a call, and a failing
    // service is logged as an error and answered with null
    private async Task<T?> TryCallAsync<T>(string what, string code,
        Func<IInvitationCodeApi, InvitationCodeRequest, Task<T>> call)
    {
        if (!IsEnabled || !LooksLikeCode(code))
        {
            return default;
        }

        try
        {
            return await call(Api, new InvitationCodeRequest(code));
        }
        catch (Exception e)
        {
            // Any failure, not only the expected ones (unreachable, non-2xx, unreadable answer): a signup must
            // never fail, or lose its cleanup, because the service misbehaved
            _logger.LogError(e, "Invitation-code {what} failed; the invitation-code service is enabled but failing", what);
            return default;
        }
    }

    private IInvitationCodeApi Api
    {
        get
        {
            var httpClient = _httpClientFactory.CreateClient($"{nameof(IInvitationCodeApi)}:{_baseUri!.Host}");
            httpClient.BaseAddress = _baseUri;
            httpClient.Timeout = TimeSpan.FromSeconds(10);
            httpClient.DefaultRequestHeaders.Add(ApiKeyHeader, _apiKey);
            return RestService.For<IInvitationCodeApi>(httpClient);
        }
    }

    //

    // https, or http to loopback, so the key is never sent in clear. Refit puts any path the URL has
    // (configuration trims its trailing /) in front of each request's.
    private static bool TryParseSafeUrl(string url, out Uri? baseUri)
    {
        baseUri = null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !(uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
        {
            return false;
        }

        baseUri = uri;
        return true;
    }
}
