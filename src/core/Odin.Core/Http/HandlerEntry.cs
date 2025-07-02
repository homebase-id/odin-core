using System;
using System.Net.Http;
using System.Threading;

namespace Odin.Core.Http;

#nullable enable

internal sealed class HandlerEntry(string handlerKey, ClientHandlerConfig handlerConfig) : IDisposable
{
    internal string HandlerKey { get; } = handlerKey;
    internal ClientHandlerConfig HandlerConfig { get; } = handlerConfig;
    internal HttpMessageHandler Handler { get; set; } = null!;
    internal DateTimeOffset Lifetime { get; } = DateTimeOffset.UtcNow.Add(handlerConfig.HandlerLifetime);
    internal bool IsExpired => DateTimeOffset.UtcNow > Lifetime;

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

