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
    private readonly HttpClient _http;
    private readonly ECDsa? _key;
    private readonly object _tokenLock = new();
    private string? _token;
    private DateTimeOffset _tokenIssuedAt;

    public ApnsVoipSender(ApnsOptions options, ILogger<ApnsVoipSender> logger)
    {
        _options = options;

        // One client for the process: APNs wants long-lived HTTP/2 connections. Rings are minutes
        // apart, so keep the connection warm rather than paying a TLS handshake per call setup,
        // and recycle it hourly so DNS changes are picked up.
        _http = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromHours(1),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(10),
            KeepAlivePingDelay = TimeSpan.FromSeconds(60),
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
        })
        {
            BaseAddress = options.Host,
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };

        if (options.IsConfigured)
        {
            // A configured but missing key is a deploy mistake; fail at startup like the Firebase key does.
            _key = ApnsProviderToken.LoadKey(File.ReadAllText(options.KeyFile));
            logger.LogInformation("APNs VoIP sending configured for {bundle} against {host}", options.BundleId, options.Host);
        }
        else
        {
            // Not an error: the relay runs without VoIP until the Apple key arrives.
            logger.LogInformation("APNs VoIP sending not configured; a Ring to an iOS device will go out as an alert push");
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

        throw new ApnsException(response.StatusCode, await ReadReasonAsync(response, cancellationToken));
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

/// <summary>An APNs rejection. <see cref="DeviceIsGone"/>: the PushKit token is dead and the host should forget it.</summary>
public sealed class ApnsException(HttpStatusCode status, string reason) : Exception($"APNs {(int)status}: {reason}")
{
    public HttpStatusCode Status { get; } = status;
    public string Reason { get; } = reason;

    public bool DeviceIsGone => Status == HttpStatusCode.Gone || Reason is "BadDeviceToken" or "Unregistered";
}
