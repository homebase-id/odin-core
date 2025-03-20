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

    public async Task<IOdinContext?> GetOrAddContextAsync(
        ClientAuthenticationToken token,
        Func<Task<IOdinContext?>> dotYouContextFactory,
        TimeSpan? expiration = null)
    {
        var duration = expiration ?? DefaultDuration;
        if (duration < TimeSpan.FromSeconds(1))
        {
            throw new OdinSystemException("Cache duration must be at least 1 second.");
        }

        var key = token.AsKey().ToString().ToLower();

        var result = await level1Cache.GetOrSetAsync(
            key,
            _ => dotYouContextFactory(),
            duration,
            _cacheTags
        );

        return result;
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


