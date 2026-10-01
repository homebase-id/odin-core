using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Odin.Core.Dto;

namespace Odin.PushNotification.Apns;

public interface IApnsVoipSender
{
    /// <summary>False until the Apple key is configured (see <see cref="ApnsOptions"/>); the router then falls back to FCM.</summary>
    bool IsConfigured { get; }

    /// <summary>Sends one VoIP push. Returns the apns-id. Throws <see cref="ApnsException"/> on an APNs rejection.</summary>
    Task<string> SendVoipAsync(DevicePushNotificationRequestV1 request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Talks to APNs directly over HTTP/2 with provider-token authentication, for the one push type
/// FCM cannot carry: PushKit VoIP. The decision to use it is made in <see cref="PushRouter"/>,
/// right before sending; this class only knows how to send.
/// </summary>
public sealed class ApnsVoipSender : IApnsVoipSender, IDisposable
{
    private readonly ApnsOptions _options;
    private readonly ILogger<ApnsVoipSender> _logger;
    private readonly HttpClient _http;
    private readonly ECDsa? _key;
    private readonly object _tokenLock = new();
    private string? _token;
    private DateTimeOffset _tokenIssuedAt;

    public ApnsVoipSender(ApnsOptions options, ILogger<ApnsVoipSender> logger, HttpMessageHandler? handler = null)
    {
        _options = options;
        _logger = logger;
        _http = handler == null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = options.Host;
        _http.DefaultRequestVersion = HttpVersion.Version20;
        _http.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;

        if (options.IsConfigured)
        {
            _key = ApnsProviderToken.LoadKey(File.ReadAllText(options.KeyFile));
            _logger.LogInformation("APNs VoIP sending configured for {bundle} against {host}", options.BundleId, options.Host);
        }
        else
        {
            // Not an error: the relay runs without VoIP until the Apple key arrives.
            _logger.LogInformation("APNs VoIP sending not configured; a Ring to an iOS device will go out as an alert push");
        }
    }

    public bool IsConfigured => _key != null;

    public async Task<string> SendVoipAsync(DevicePushNotificationRequestV1 request, CancellationToken cancellationToken = default)
    {
        if (_key == null)
        {
            throw new InvalidOperationException("APNs is not configured");
        }

        var message = ApnsVoipMessage.Build(request, _options.BundleId, DateTimeOffset.UtcNow);

        using var http = new HttpRequestMessage(HttpMethod.Post, message.Path);
        http.Version = HttpVersion.Version20;
        http.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
        http.Headers.Authorization = new AuthenticationHeaderValue("bearer", CurrentToken());
        foreach (var (name, value) in message.Headers)
        {
            http.Headers.TryAddWithoutValidation(name, value);
        }
        http.Content = new StringContent(message.Body, Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(http, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return response.Headers.TryGetValues("apns-id", out var ids) ? ids.First() : request.Id;
        }

        var reason = await ReadReasonAsync(response, cancellationToken);
        throw new ApnsException(response.StatusCode, reason);
    }

    private string CurrentToken()
    {
        lock (_tokenLock)
        {
            var now = DateTimeOffset.UtcNow;
            if (_token == null || now - _tokenIssuedAt > ApnsProviderToken.Lifetime)
            {
                _token = ApnsProviderToken.Create(_key!, _options.KeyId, _options.TeamId, now);
                _tokenIssuedAt = now;
            }
            return _token;
        }
    }

    private static async Task<string> ReadReasonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var json = JsonDocument.Parse(body);
            return json.RootElement.TryGetProperty("reason", out var reason) ? reason.GetString() ?? "" : body;
        }
        catch
        {
            return response.ReasonPhrase ?? response.StatusCode.ToString();
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        _key?.Dispose();
    }
}

/// <summary>An APNs rejection. <see cref="DeviceIsGone"/> is what the host treats as "remove the subscription".</summary>
public sealed class ApnsException(HttpStatusCode status, string reason) : Exception($"APNs {(int)status}: {reason}")
{
    public HttpStatusCode Status { get; } = status;
    public string Reason { get; } = reason;

    public bool DeviceIsGone => Status == HttpStatusCode.Gone || Reason is "BadDeviceToken" or "Unregistered" or "DeviceTokenNotForTopic";
}
