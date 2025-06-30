using System;
using System.Collections.Generic;
using System.IO.Hashing;
using System.Linq;
using System.Net.Http;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Odin.Core.Http;

#nullable enable

//
// This is a variation of dotnet v9's IHttpClientFactory and DefaultClientFactory.
// The primary reason for it, is to solve the problem of IHttpClientFactory only being able to register http clients
// during startup, when DI services are being registered.
// Since this project is multi-tenant and tenants and new end-points can come and go, we have to be able to
// create (and reuse) a new http client on the fly.
//
// In the current implementation, this code has the same potential issues with disposal as IHttpClientFactory:
// once a handler's lifetime expires, it is possible for it to be disposed of while still being used by a
// HttpClient instance.
//

//
// HandlerLifetime:
//   - How long a handler is kept alive before being disposed.
//   - No DNS update checks are performed during this time.
//

//
// Usage:
//
// var client = factory.CreateClient("example.com", config =>
// {
//    config.HandlerLifetime = TimeSpan.FromMinutes(2);
// });
//
// var response = await client.GetAsync("https://www.example.com");
//

//
// BEWARE BEWARE BEWARE BEWARE!
//
// Don't use the same client remoteHostName for different remote hosts!
//

//
// Prompt for AI:
// Please review this code, comparing it to the IHttpClientFactory and DefaultClientFactory implementations in .NET 9. At this point I'm not interested in performance or nitpicks, unless you spot something seriously wrong. Pay special attention to the comment in the top that explains what the purpose for this code is.
//

public interface IDynamicHttpClientFactory : IDisposable
{
    HttpClient CreateClient(string remoteHostName, Action<ClientConfig>? configure = null);
}

//

public sealed class DynamicHttpClientFactory : IDynamicHttpClientFactory
{
    private readonly ILogger<DynamicHttpClientFactory> _logger;
    private readonly ReaderWriterLockSlim _rwLock = new ();
    private readonly Dictionary<string, HandlerEntry> _activeHandlers = new ();
    private readonly Dictionary<Guid, HandlerEntry> _expiredHandlers = new ();
    private readonly CancellationTokenSource _cts = new ();
    private readonly TimeSpan _defaultHandlerLifetime = TimeSpan.FromMinutes(2);
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _cleanupGracePeriod = TimeSpan.FromMinutes(2);
    private readonly Task _cleanupTask;
    private volatile bool _disposed;

    //

    public DynamicHttpClientFactory(ILogger<DynamicHttpClientFactory> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _cleanupTask = Task.Run(CleanupLoopAsync, _cts.Token);
    }

    //

    public HttpClient CreateClient(string remoteHostName, Action<ClientConfig>? configure = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(DynamicHttpClientFactory));
        ArgumentException.ThrowIfNullOrEmpty(remoteHostName, nameof(remoteHostName));

        var config = new ClientConfig();
        configure?.Invoke(config);
        
        var handlerKey = remoteHostName + "-" + config.GetHashedString();
        var handler = GetOrCreateHandler(handlerKey, config);
        var client = new HttpClient(handler, disposeHandler: false);

        _logger.LogDebug("Created HttpClient for {remoteHostName} with handler key {key}", remoteHostName, handlerKey);

        return client;
    }

    //

    private HttpMessageHandler GetOrCreateHandler(string handlerKey, ClientConfig config)
    {
        HandlerEntry? entry;

        _rwLock.EnterReadLock();
        try
        {
            if (_activeHandlers.TryGetValue(handlerKey, out entry) && !entry.IsExpired)
            {
                return entry.Handler;
            }
        }
        finally
        {
            _rwLock.ExitReadLock();
        }

        _rwLock.EnterWriteLock();
        try
        {
            if (_activeHandlers.TryGetValue(handlerKey, out entry) && !entry.IsExpired)
            {
                return entry.Handler;
            }

            var clientHandler = new HttpClientHandler();
            ConfigureClientHandler(clientHandler, config);

            HttpMessageHandler messageHandler = clientHandler;
            foreach (var factory in config.CustomHandlerFactories.AsEnumerable().Reverse())
            {
                messageHandler = factory(messageHandler);
            }

            if (config.HandlerLifetime <= TimeSpan.Zero)
            {
                throw new ArgumentException("HandlerLifetime must be positive", nameof(config));
            }

            var handlerLifetime = config.HandlerLifetime != TimeSpan.Zero
                ? config.HandlerLifetime
                : _defaultHandlerLifetime;

            var newEntry = new HandlerEntry(handlerKey, config, handlerLifetime);
            messageHandler = new RequestTrackingHandler(_logger, messageHandler, newEntry);
            newEntry.Handler = messageHandler;

            _activeHandlers[handlerKey] = newEntry;

            if (entry is { IsExpired: true })
            {
                _expiredHandlers[Guid.NewGuid()] = entry;
            }

            _logger.LogDebug("Created new handler for key {key}", handlerKey);

            return messageHandler;
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }

    //

    private static void ConfigureClientHandler(HttpClientHandler handler, ClientConfig config)
    {
        if (config.ClientCertificate != null)
        {
            handler.ClientCertificates.Add(config.ClientCertificate);
        }

        handler.AllowAutoRedirect = false; // We don't want auto redirects to avoid unexpected behavior.
        handler.UseCookies = false; // We don't want cookies since they can't be shared across tenants.
        handler.UseProxy = false; // No proxy support for now.
        handler.SslProtocols = SslProtocols.None; // No specific SSL protocols set, defaults will be used.

        if (config.AllowUntrustedServerCertificate)
        {
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        }
    }

    //

    private async Task CleanupLoopAsync()
    {
        var keysToRemove = new List<string>();
        var guidsToRemove = new List<Guid>();

        while (!_cts.Token.IsCancellationRequested)
        {
            _logger.LogDebug("Starting cleanup for expired HTTP handlers");
            try
            {
                _rwLock.EnterWriteLock();
                try
                {
                    //
                    // Move expired handlers to the expired list
                    //

                    keysToRemove.Clear();
                    foreach (var pair in _activeHandlers)
                    {
                        if (pair.Value.IsExpired)
                        {
                            keysToRemove.Add(pair.Key);
                        }
                    }
                    foreach (var key in keysToRemove)
                    {
                        if (_activeHandlers.Remove(key, out var entry))
                        {
                            _expiredHandlers[Guid.NewGuid()] = entry;
                        }
                    }

                    //
                    // Dispose expired handlers after grace period if not in use
                    //

                    guidsToRemove.Clear();
                    foreach (var pair in _expiredHandlers)
                    {
                        var handler = pair.Value;
                        if (handler.CanDispose && DateTimeOffset.UtcNow > pair.Value.Expiration + _cleanupGracePeriod)
                        {
                            guidsToRemove.Add(pair.Key);
                        }
                    }
                    foreach (var guid in guidsToRemove)
                    {
                        if (_expiredHandlers.Remove(guid, out var entry))
                        {
                            try
                            {
                                entry.Dispose();
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Dispose error: {message}", ex.Message);
                            }
                        }
                    }
                }
                finally
                {
                    _rwLock.ExitWriteLock();
                }

                await Task.Delay(_cleanupInterval, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cleanup error: {message}", ex.Message);
            }
            _logger.LogDebug("Finished cleanup for expired HTTP handlers");
        }
        _logger.LogDebug("Exiting OdinHttpClientFactory cleanup loop");
    }

    //

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _cts.Cancel();

            try
            {
                _cleanupTask.Wait(TimeSpan.FromSeconds(30));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Timeout or error waiting for cleanup task during disposal");
            }

            _rwLock.EnterWriteLock();
            try
            {
                foreach (var entry in _expiredHandlers.Values)
                {
                    try
                    {
                        entry.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Dispose error: {message}", ex.Message);
                    }
                }
                _expiredHandlers.Clear();

                foreach (var entry in _activeHandlers.Values)
                {
                    try
                    {
                        entry.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Dispose error: {message}", ex.Message);
                    }
                }
                _activeHandlers.Clear();
            }
            finally
            {
                _rwLock.ExitWriteLock();
            }

            _cts.Dispose();
            _rwLock.Dispose();
        }
    }
}

//

public sealed class ClientConfig
{
    //
    // HttpMessageHandler-level settings (affect handler sharing/creation)
    // Make sure to set these in OdinHttpClientFactory.ConfigureHandler method
    //
    public bool AllowUntrustedServerCertificate { get; set; } = false;
    public X509Certificate2? ClientCertificate { get; set; }
    public TimeSpan HandlerLifetime { get; set; } = TimeSpan.FromMinutes(2);

    // Handler "middleware" factories
    public List<Func<HttpMessageHandler, HttpMessageHandler>> CustomHandlerFactories { get; set; } = [];

    //

    public string GetHashedString()
    {
        var serialized = SerializeForHashing();
        var bytes = Encoding.UTF8.GetBytes(serialized);
        var hashBytes = XxHash64.Hash(bytes);
        return Convert.ToBase64String(hashBytes);
    }

    //

    private string SerializeForHashing()
    {
        var sb = new StringBuilder();
        sb.Append(AllowUntrustedServerCertificate);
        sb.Append(ClientCertificate?.Thumbprint ?? "");
        sb.Append(CustomHandlerFactories.Count == 0 ? "" : "not-reliable-for-hashing");
        sb.Append(HandlerLifetime.Ticks);
        return sb.ToString();
    }

    //

}

//

public sealed class HandlerEntry(string handlerKey, ClientConfig clientConfig, TimeSpan lifetime) : IDisposable
{
    internal string HandlerKey { get; } = handlerKey;
    internal ClientConfig ClientConfig { get; } = clientConfig;
    internal HttpMessageHandler Handler { get; set; } = null!;
    internal DateTimeOffset Expiration { get; } = DateTimeOffset.UtcNow.Add(lifetime);
    internal bool IsExpired => DateTimeOffset.UtcNow > Expiration;

    internal int ActiveRequests => _activeRequests;
    private int _activeRequests;
    internal void IncrementActiveRequests() => Interlocked.Increment(ref _activeRequests);
    internal void DecrementActiveRequests() => Interlocked.Decrement(ref _activeRequests);
    internal bool CanDispose => IsExpired && Interlocked.CompareExchange(ref _activeRequests, 0, 0) == 0;

    private bool _disposed;
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            Handler.Dispose();
        }
    }
}

//

internal sealed class RequestTrackingHandler(ILogger logger, HttpMessageHandler innerHandler, HandlerEntry entry)
    : DelegatingHandler(innerHandler)
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        entry.IncrementActiveRequests();
        try
        {
            if (logger.IsEnabled(LogLevel.Trace))
            {
                logger.LogTrace("Active requests for handler {key}: {count}", entry.HandlerKey, entry.ActiveRequests);
                logger.LogTrace("CanDispose for handler {key}: {canDispose}", entry.HandlerKey, entry.CanDispose);
            }
            return await base.SendAsync(request, cancellationToken);
        }
        finally
        {
            entry.DecrementActiveRequests();
            if (logger.IsEnabled(LogLevel.Trace))
            {
                logger.LogTrace("Active requests for handler {key}: {count}", entry.HandlerKey, entry.ActiveRequests);
                logger.LogTrace("CanDispose for handler {key}: {canDispose}", entry.HandlerKey, entry.CanDispose);
            }
        }

    }

    //

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        entry.IncrementActiveRequests();
        try
        {
            if (logger.IsEnabled(LogLevel.Trace))
            {
                logger.LogTrace("Active requests for handler {key}: {count}", entry.HandlerKey, entry.ActiveRequests);
                logger.LogTrace("CanDispose for handler {key}: {canDispose}", entry.HandlerKey, entry.CanDispose);
            }
            return base.Send(request, cancellationToken);
        }
        finally
        {
            entry.DecrementActiveRequests();
            if (logger.IsEnabled(LogLevel.Trace))
            {
                logger.LogTrace("Active requests for handler {key}: {count}", entry.HandlerKey, entry.ActiveRequests);
                logger.LogTrace("CanDispose for handler {key}: {canDispose}", entry.HandlerKey, entry.CanDispose);
            }
        }
    }

}