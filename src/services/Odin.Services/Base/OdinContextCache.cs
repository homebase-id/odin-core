using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Cache;
using Odin.Services.Authorization.ExchangeGrants;
using StackExchange.Redis;

namespace Odin.Services.Base;

#nullable enable

public class OdinContextCache(
    ILogger<OdinContextCache> logger,
    CacheKeyPrefix cacheKeyPrefix,
    ITenantLevel1Cache<OdinContextCache> level1Cache)
    : IDisposable
{
    private static readonly TimeSpan DefaultDuration = TimeSpan.FromMinutes(60);
    private readonly List<string> _cacheTags = [Guid.NewGuid().ToString()];
    private const string InvalidateMessage = "cache_invalidation";
    private readonly RedisChannel _channel = new(cacheKeyPrefix, RedisChannel.PatternMode.Literal);
    private ISubscriber? _pubSub;

    //

    internal async Task InitializePubSub(IConnectionMultiplexer redis)
    {
        if (_pubSub != null)
        {
            return;
        }

        _pubSub = redis.GetSubscriber();
        await _pubSub.SubscribeAsync(_channel, async void (channel, message) =>
        {
            try
            {
                if (message == InvalidateMessage)
                {
                    await level1Cache.RemoveByTagAsync(_cacheTags);
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "{message}", e.Message);
            }
        });
    }

    //

    /// <param name="scope">Set for a second context the same token gets on other routes, keeping it apart from the token's own</param>
    public Task<IOdinContext?> GetOrAddContextAsync(
        ClientAuthenticationToken token,
        Func<Task<IOdinContext?>> dotYouContextFactory,
        string? scope = null)
    {
        return GetOrAddContextAsync(token, async () => (await dotYouContextFactory(), null), scope);
    }

    /// <summary>
    /// As above, but the factory says how long its result may be cached, for a context whose credential
    /// it only learns the end of while building it: a cached context must not outlive the token it
    /// was built for. Null means the default; a longer value is capped at the default.
    /// </summary>
    public async Task<IOdinContext?> GetOrAddContextAsync(
        ClientAuthenticationToken token,
        Func<Task<(IOdinContext? Context, TimeSpan? CacheFor)>> dotYouContextFactory,
        string? scope = null)
    {
        var key = GetCacheKey(token, scope);

        // SEB:NOTE deliberately not using GetOrSetAsync here to avoid dealing with exceptions thrown by the factory.
        // Two builds for the same token give the same context, except across a ResetAsync: a context built from the
        // permissions before a reset must not be stored after it, so the set is skipped when the cache removed
        // anything since the build started (#1909).
        var result = await level1Cache.GetOrDefaultAsync<IOdinContext?>(key);
        if (result == null)
        {
            var buildStarted = await level1Cache.BeginReadAsync();
            (result, var cacheFor) = await dotYouContextFactory();
            var duration = cacheFor < DefaultDuration ? cacheFor.Value : DefaultDuration;

            // Less than the cache's minimum is not "cache briefly", it is an error there. The
            // credential is about to end anyway; serve this request from the fresh build and stop.
            if (result != null && duration >= FusionCacheWrapper.MinL2Duration)
            {
                await level1Cache.SetIfNotRemovedSinceAsync(key, result, buildStarted, duration, EntrySize.Small, _cacheTags);
            }
        }

        return result;
    }

    /// <summary>
    /// The token's type is part of the key: it is a byte the caller sends, and each type is resolved
    /// by its own handler, so a context built for one type must never be found under another.
    /// </summary>
    internal static string GetCacheKey(ClientAuthenticationToken token, string? scope)
    {
        var key = $"{token.AsKey().ToString().ToLower()}:{(int)token.ClientTokenType}";
        return scope == null ? key : $"{key}:{scope}";
    }

    //

    public async Task ResetAsync()
    {
        await level1Cache.RemoveByTagAsync(_cacheTags);
        if (_pubSub != null)
        {
            await _pubSub.PublishAsync(_channel, InvalidateMessage);
        }
    }

    //

    public void Dispose()
    {
        _pubSub?.Unsubscribe(_channel);
        _pubSub = null;
    }

    //
}


