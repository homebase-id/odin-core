using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Odin.Core.Http;

//
// IDynamicHttpClientFactory is a variation of dotnet v9's IHttpClientFactory and DefaultClientFactory.
// The primary reason for it is to solve the problem of IHttpClientFactory only being able to register http clients
// during startup, when DI services are being registered. Since this project is multi-tenant and tenants and new
// end-points can come and go, we have to be able to create (and reuse) a new http client on the fly.
//
// Note that in the current implementation, this code has the same potential issues with disposal as IHttpClientFactory:
// once a handler's lifetime expires, it is possible for it to be disposed of while still being used by a
// HttpClient instance.
//

//
// IDynamicHttpClientFactory rules when creating a HttpClient:
// - It is HttpClientHandler instance that is managed by DynamicHttpClientFactory, not the HttpClient instance.
// - The HttpClientHandler instance, which is explicitly or implicitly attached to a HttpClient instance,
//   is shared by different HttpClient instances across all threads.
// - It is OK to change properties on the HttpClient instance (e.g. AddDefaultHeaders)
//   as long as you make sure that the instance is short-lived and not mutated on another thread.
// - It is OK to create a HttpClientHandler, but it *MUST NOT* hold any instance data. This includes
//   cookies in a CookieContainer. Therefore, avoid using Cookies. If you need cookies, set the headers
//   manually.
// - Use HandlerLifetime to control how long connections are pooled.
// - As long as a connection is pooled, no DNS updates will be visible on that connection.
//

//
// Usage:
//
// var client = factory.CreateClient("www.example.com", config =>
// {
//    config.HandlerLifetime = TimeSpan.FromMinutes(2);
// });
//
// var response = await client.GetAsync("https://www.example.com");
//

//
// BEWARE BEWARE BEWARE BEWARE!
//
// - Don't use the same client remoteHostKey for different remote hosts.
// - Change remoteHostKey for SAME hosts if you have different configurations for them.
// - If you add one or more MessageHandler chains, make sure to use a unique remoteHostKey for each configuration.
//   The chains are not included in the handler lifetime hash, so handlers with otherwise identical configurations
//   can clash.
//

//
// Prompt for AI review:
// Please review this code, comparing it to the IHttpClientFactory and DefaultClientFactory implementations in .NET 9. At this point I'm not interested in performance or nitpicks, unless you spot something seriously wrong. Pay special attention to the comment in the top that explains what the purpose for this code is.
//

//

#nullable enable

public interface IDynamicHttpClientFactory : IDisposable
{
    HttpClient CreateClient(string remoteHostKey, Action<ClientHandlerConfig>? configure = null);
}

//

public sealed class DynamicHttpClientFactory : IDynamicHttpClientFactory
{
    private static readonly TimeSpan DefaultHandlerLifetime = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan DefaultCleanupInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultDisposeGracePeriod = TimeSpan.FromMinutes(2);
    private readonly ReaderWriterLockSlim _rwLock = new ();
    private readonly Dictionary<string, HandlerEntry> _activeHandlers = new ();
    private readonly Dictionary<Guid, HandlerEntry> _expiredHandlers = new ();
    private readonly CancellationTokenSource _cts = new ();
    private readonly ILogger<DynamicHttpClientFactory> _logger;
    private readonly TimeSpan _defaultHandlerLifetime;
    private readonly TimeSpan _cleanupInterval;
    private readonly TimeSpan _disposeGracePeriod;
    private readonly Task _cleanupTask;
    private volatile bool _disposed;

    //

    public DynamicHttpClientFactory(
        ILogger<DynamicHttpClientFactory> logger,
        TimeSpan? defaultHandlerLifetime = null,
        TimeSpan? cleanupInterval = null,
        TimeSpan? disposeGracePeriod = null)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _defaultHandlerLifetime = defaultHandlerLifetime ?? DefaultHandlerLifetime;
        _cleanupInterval = cleanupInterval ?? DefaultCleanupInterval;
        _disposeGracePeriod = disposeGracePeriod ?? DefaultDisposeGracePeriod;
        
        _cleanupTask = Task.Run(CleanupLoopAsync, _cts.Token);
    }

    //

    public HttpClient CreateClient(string remoteHostKey, Action<ClientHandlerConfig>? configure = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(DynamicHttpClientFactory));
        ArgumentException.ThrowIfNullOrEmpty(remoteHostKey, nameof(remoteHostKey));

        var config = new ClientHandlerConfig();
        configure?.Invoke(config);
       
        if (config.HandlerLifetime <= TimeSpan.Zero)
        {
            config.HandlerLifetime = _defaultHandlerLifetime;
        }
        
        var handlerKey = remoteHostKey + "-" + config.GetHashedString();
        var handler = GetOrCreateHandler(remoteHostKey, handlerKey, config);
        var client = new HttpClient(handler, disposeHandler: false);

        _logger.LogTrace("Created HttpClient for {remoteHostKey} with handler key {key}", remoteHostKey, handlerKey);

        return client;
    }

    //

    private HttpMessageHandler GetOrCreateHandler(string remoteHostKey, string handlerKey, ClientHandlerConfig handlerConfig)
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
            ConfigureClientHandler(remoteHostKey, clientHandler, handlerConfig);

            HttpMessageHandler messageHandler = clientHandler;
            foreach (var factory in handlerConfig.MessageHandlerChain.AsEnumerable().Reverse())
            {
                messageHandler = factory(messageHandler);
            }

            var newEntry = new HandlerEntry(handlerKey, handlerConfig);
            messageHandler = new RequestTrackingHandler(_logger, messageHandler, newEntry);
            newEntry.Handler = messageHandler;

            _activeHandlers[handlerKey] = newEntry;

            if (entry is { IsExpired: true })
            {
                _expiredHandlers[Guid.NewGuid()] = entry;
            }

            _logger.LogTrace("Created new handler for key {key} with lifetime {lifetime}", handlerKey, handlerConfig.HandlerLifetime);

            return messageHandler;
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }

    //

    private void ConfigureClientHandler(string remoteHostKey, HttpClientHandler handler, ClientHandlerConfig handlerConfig)
    {
        if (handlerConfig.ClientCertificate != null)
        {
            handler.ClientCertificates.Add(handlerConfig.ClientCertificate);
        }

        handler.AllowAutoRedirect = false; // We don't want auto redirects to avoid handler/connection pollution.
        handler.UseCookies = false; // We don't want cookies since they can't be shared across tenants.
        handler.UseProxy = false; // No proxy support for now.
        handler.SslProtocols = SslProtocols.None; // No specific SSL protocols set, defaults will be used.

        if (handlerConfig.AllowUntrustedServerCertificate)
        {
            _logger.LogWarning("Allowing untrusted server certificates for {remoteHostKey} handler {key}",
                remoteHostKey,
                handlerConfig.GetHashedString());
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        }
    }

    //

    public int CountActiveHandlers()
    {
        _rwLock.EnterReadLock();
        try
        {
            return _activeHandlers.Count;
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    //

    public int CountExpiredHandlers()
    {
        _rwLock.EnterReadLock();
        try
        {
            return _expiredHandlers.Count;
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    //

    private async Task CleanupLoopAsync()
    {
        var keysToRemove = new List<string>();
        var guidsToRemove = new List<Guid>();

        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_cleanupInterval, _cts.Token);
                _logger.LogTrace("Starting cleanup for expired HTTP handlers");

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
                            _logger.LogTrace("Moving handler {key} from active to expired", pair.Key);
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
                        if (handler.CanDispose && DateTimeOffset.UtcNow > pair.Value.Lifetime + _disposeGracePeriod)
                        {
                            _logger.LogTrace("Preparing handler disposal {key}", pair.Key);
                            guidsToRemove.Add(pair.Key);
                        }
                    }
                    foreach (var guid in guidsToRemove)
                    {
                        if (_expiredHandlers.Remove(guid, out var entry))
                        {
                            _logger.LogDebug("Disposing HttpClient handler {key}", entry.HandlerKey);
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
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cleanup error: {message}", ex.Message);
            }

            _logger.LogTrace("Finished cleanup for expired HTTP handlers");
        }
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

    //

}
