using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Hashing;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Odin.Core.Http;

#nullable enable

public interface IOdinHttpClientFactory : IDisposable
{
    // SEB:TODO
}

//

public sealed class OdinHttpClientFactory : IOdinHttpClientFactory
{
    private readonly ILogger<OdinHttpClientFactory> _logger;
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

    public OdinHttpClientFactory(ILogger<OdinHttpClientFactory> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _cleanupTask = Task.Run(CleanupLoopAsync, _cts.Token);
    }

    //

    public HttpClient CreateClient(string name, Action<ClientConfig>? configure = null)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(OdinHttpClientFactory));
        }

        ArgumentException.ThrowIfNullOrEmpty(name, nameof(name));

        var config = new ClientConfig();
        configure?.Invoke(config);
        
        var handlerKey = name + "-" + config.GetHashedString();
        var handler = GetOrCreateHandler(handlerKey, config);
        var client = new HttpClient(handler, disposeHandler: false);

        if (config.BaseAddress != null)
        {
            client.BaseAddress = config.BaseAddress;
        }

        foreach (var header in config.DefaultHeaders)
        {
            client.DefaultRequestHeaders.Add(header.Key, header.Value);
        }

        client.Timeout = config.Timeout;

        _logger.LogDebug("Created HttpClient for {name} with handler key {key}", name, handlerKey);

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

        HttpMessageHandler? messageHandler;
        _rwLock.EnterWriteLock();
        try
        {
            if (_activeHandlers.TryGetValue(handlerKey, out entry) && !entry.IsExpired)
            {
                return entry.Handler;
            }

            messageHandler = new HttpClientHandler();
            foreach (var factory in config.CustomHandlerFactories.AsEnumerable().Reverse())
            {
                messageHandler = factory(messageHandler);
            }

            var handlerLifetime = config.HandlerLifetime != TimeSpan.Zero
                ? config.HandlerLifetime
                : _defaultHandlerLifetime;

            var newEntry = new HandlerEntry(handlerKey, config, messageHandler, handlerLifetime);
            _activeHandlers[handlerKey] = newEntry;

            if (entry is { IsExpired: true })
            {
                _expiredHandlers[Guid.NewGuid()] = entry;
            }

            _logger.LogDebug("Created new handler for key {key}", handlerKey);
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }

        return messageHandler;
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
                    // Move expired handlers to the expired list
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

                    // Dispose expired handlers after grace period
                    guidsToRemove.Clear();
                    foreach (var pair in _expiredHandlers)
                    {
                        if (DateTimeOffset.UtcNow > pair.Value.Expiration + _cleanupGracePeriod)
                        {
                            guidsToRemove.Add(pair.Key);
                        }
                    }
                    foreach (var guid in guidsToRemove)
                    {
                        if (_expiredHandlers.Remove(guid, out var entry))
                        {
                            entry.Dispose();
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
                    entry.Dispose();
                }
                _expiredHandlers.Clear();

                foreach (var entry in _activeHandlers.Values)
                {
                    entry.Dispose();
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
    public Uri? BaseAddress { get; set; }
    public Dictionary<string, string> DefaultHeaders { get; set; } = new ();
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(100);
    public TimeSpan HandlerLifetime { get; set; } = TimeSpan.FromMinutes(2);
    public X509Certificate2? ClientCertificate { get; set; }
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
        sb.Append(BaseAddress?.ToString() ?? "");
        sb.Append(Timeout.Ticks);
        sb.Append(HandlerLifetime.Ticks);
        sb.Append(ClientCertificate?.Thumbprint ?? "");
        // CustomHandlerFactories is excluded from hash due to unreliable delegate comparison
        sb.Append(CustomHandlerFactories.Count == 0 ? "" : "ignored-for-hashing");
        foreach (var kvp in DefaultHeaders.OrderBy(k => k.Key))
        {
            sb.Append(kvp.Key);
            sb.Append(kvp.Value ?? "");
        }
        return sb.ToString();
    }

    //

}

//

public sealed class HandlerEntry(
    string handlerKey,
    ClientConfig clientConfig,
    HttpMessageHandler handler,
    TimeSpan lifetime) : IDisposable
{
    public string HandlerKey { get; } = handlerKey;
    public ClientConfig ClientConfig { get; } = clientConfig;
    public HttpMessageHandler Handler { get; } = handler;
    public DateTimeOffset Expiration { get; } = DateTimeOffset.UtcNow.Add(lifetime);
    public bool IsExpired => DateTimeOffset.UtcNow > Expiration;
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
