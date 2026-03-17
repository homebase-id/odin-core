using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Cache;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database;

#nullable enable

public interface ITransactionalCacheFactory
{
    public TransactionalCache Create(string keyPrefix, string rootTag);
}

public abstract class AbstractTransactionalCacheFactory(
    ILevel2Cache cache,
    ITransactionalCacheStats cacheStats,
    IScopedConnectionFactory scopedConnectionFactory)
{
    public TransactionalCache Create(string keyPrefix, string rootTag)
    {
        return new TransactionalCache(cache, cacheStats, scopedConnectionFactory, keyPrefix, rootTag);
    }
}

//

public sealed class TransactionalCache
{
    public const long DefaultEntrySize = EntrySize.Medium;

    private long _hits;
    public long Hits => Interlocked.Read(ref _hits);

    private long _misses;
    public long Misses => Interlocked.Read(ref _misses);

    private readonly ILevel2Cache _cache;
    private readonly ITransactionalCacheStats _cacheStats;
    private readonly IScopedConnectionFactory _scopedConnectionFactory;
    private readonly string _keyPrefix;

    private readonly List<string> _rootTag;
    private string RootTag { get; }

    private bool InDatabaseTransaction => _scopedConnectionFactory.HasTransaction;

    //

    public TransactionalCache(
        ILevel2Cache cache,
        ITransactionalCacheStats cacheStats,
        IScopedConnectionFactory scopedConnectionFactory,
        string keyPrefix,
        string rootTag)
    {
        ArgumentNullException.ThrowIfNull(cache, nameof(cache));
        ArgumentNullException.ThrowIfNull(scopedConnectionFactory, nameof(scopedConnectionFactory));
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPrefix, nameof(keyPrefix));
        ArgumentException.ThrowIfNullOrWhiteSpace(rootTag, nameof(rootTag));

        _cache = cache;
        _cacheStats = cacheStats;
        _scopedConnectionFactory = scopedConnectionFactory;
        _keyPrefix = keyPrefix;
        _rootTag = [rootTag];
        RootTag = _rootTag.First();
    }

    //

    public Task<TValue> GetOrSetAsync<TValue>(
        string key,
        Func<CancellationToken, Task<TValue>> factory,
        TimeSpan ttl,
        long entrySize = DefaultEntrySize,
        List<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        return InternalGetOrSetAsync(
            key,
            factory,
            ttl,
            value => value is null ? EntrySize.Small : entrySize,
            tags,
            cancellationToken);
    }

    //

    public Task<TValue> GetOrSetAsync<TValue>(
        byte[] key,
        Func<CancellationToken, Task<TValue>> factory,
        TimeSpan ttl,
        long entrySize = DefaultEntrySize,
        List<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        return InternalGetOrSetAsync(
            key.ToHexString(),
            factory,
            ttl,
            value => value is null ? EntrySize.Small : entrySize,
            tags,
            cancellationToken);
    }

    //

    public Task<List<TItem>> GetOrSetListAsync<TItem>(
        string key,
        Func<CancellationToken, Task<List<TItem>>> factory,
        TimeSpan ttl,
        long entrySize = DefaultEntrySize,
        List<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        return InternalGetOrSetAsync(
            key,
            factory,
            ttl,
            list => list.Count == 0 ? EntrySize.Small : entrySize * list.Count,
            tags,
            cancellationToken);
    }

    //

    public Task<List<TItem>> GetOrSetListAsync<TItem>(
        byte[] key,
        Func<CancellationToken, Task<List<TItem>>> factory,
        TimeSpan ttl,
        long entrySize = DefaultEntrySize,
        List<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        return InternalGetOrSetAsync(
            key.ToHexString(),
            factory,
            ttl,
            list => list.Count == 0 ? EntrySize.Small : entrySize * list.Count,
            tags,
            cancellationToken);
    }

    //

    private async Task<TValue> InternalGetOrSetAsync<TValue>(
        string key,
        Func<CancellationToken, Task<TValue>> factory,
        TimeSpan ttl,
        Func<TValue, long> entrySizeFactory,
        List<string>? tags,
        CancellationToken cancellationToken)
    {
        if (ttl <= TimeSpan.Zero)
        {
            throw new OdinSystemException("TTL must be positive.");
        }

        // Bypass cache if connection is in a transaction
        if (InDatabaseTransaction)
        {
            return await factory(cancellationToken);
        }

        var cacheKey = BuildCacheKey(key);
        var hit = true;
        var result = await _cache.GetOrSetAsync(
            cacheKey,
            _ =>
            {
                hit = false;
                return factory(cancellationToken);
            },
            ttl,
            entrySizeFactory,
            CombineAllTagsWithRoot(tags),
            cancellationToken);

        if (hit)
        {
            Interlocked.Increment(ref _hits);
            _cacheStats.ReportHit(cacheKey);
        }
        else
        {
            Interlocked.Increment(ref _misses);
            _cacheStats.ReportMiss(cacheKey);
        }

        return result;
    }

    //

    #region Poisoned pill overloads

    //
    // DO NOT REMOVE THESE OVERLOADS!
    //
    // They prevent callers from accidentally using GetOrSetAsync<TValue> with TValue = List<T>.
    // Without them the compiler happily resolves List<T> against the generic GetOrSetAsync<TValue>
    // overload, bypassing GetOrSetListAsync entirely. That matters because GetOrSetListAsync
    // calculates entrySize based on the number of items in the list, which is critical for correct
    // memory cache sizing. C# overload resolution cannot prefer List<TItem> over TValue automatically
    // (both have one type parameter), so we use [Obsolete(error: true)] to force a compile error
    // that guides callers to GetOrSetListAsync.
    //

    //

    // DO NOT REMOVE THESE Obsolete OVERLOADS!
    [Obsolete("Use GetOrSetListAsync for list types", error: true)]
    public Task<List<TItem>> GetOrSetAsync<TItem>(
        string key,
        Func<CancellationToken, Task<List<TItem>>> factory,
        TimeSpan ttl,
        long entrySize = DefaultEntrySize,
        List<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    // DO NOT REMOVE THESE Obsolete OVERLOADS!
    [Obsolete("Use GetOrSetListAsync for list types", error: true)]
    public Task<List<TItem>> GetOrSetAsync<TItem>(
        byte[] key,
        Func<CancellationToken, Task<List<TItem>>> factory,
        TimeSpan ttl,
        long entrySize = DefaultEntrySize,
        List<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    #endregion

    //

    public Func<Task> CreateRemoveByKeyAction(string key)
    {
        return () => _cache.RemoveAsync(BuildCacheKey(key)).AsTask();
    }

    //

    public Func<Task> CreateRemoveByKeyAction(byte[] key)
    {
        return CreateRemoveByKeyAction(key.ToHexString());
    }

    //

    public Func<Task> CreateRemoveByTagsAction(IEnumerable<string> tags)
    {
        var explicitTags = CombineAllTagsWithoutRoot(tags.ToList());
        return () => _cache.RemoveByTagAsync(explicitTags).AsTask();
    }
    
    //

    public async Task InvalidateAllAsync()
    {
        if (InDatabaseTransaction)
        {
            _scopedConnectionFactory.AddPostCommitAction(async () => await _cache.RemoveByTagAsync(_rootTag));
            _scopedConnectionFactory.AddPostRollbackAction(async () => await _cache.RemoveByTagAsync(_rootTag));
        }
        else
        {
            await _cache.RemoveByTagAsync(_rootTag);
        }
    }

    //

    public async Task InvalidateAsync(IEnumerable<Func<Task>> invalidateActions)
    {
        var actionsArray = invalidateActions.ToArray();

        if (actionsArray.Length == 0)
        {
            return;
        }

        if (InDatabaseTransaction)
        {
            _scopedConnectionFactory.AddPostCommitAction(async () => await Task.WhenAll(actionsArray.Select(a => a())));
            _scopedConnectionFactory.AddPostRollbackAction(async () => await Task.WhenAll(actionsArray.Select(a => a())));
        }
        else
        {
            await Task.WhenAll(actionsArray.Select(a => a()));
        }
    }

    //

    private string BuildCacheKey(string key)
    {
        return _keyPrefix + ":" + key;
    }

    //

    private List<string> BuildTags(List<string> tags)
    {
        if (tags.Count == 0)
        {
            return tags;
        }

        var result = new List<string>(tags.Count);
        foreach (var tag in tags)
        {
            result.Add(RootTag + ":" + tag);
        }
        return result;
    }

    //

    private List<string> CombineAllTagsWithRoot(List<string>? tags)
    {
        if (tags == null || tags.Count == 0)
        {
            return _rootTag;
        }

        var result = new List<string>(_rootTag.Count + tags.Count);
        result.AddRange(_rootTag);
        result.AddRange(BuildTags(tags));
        return result;
    }

    //

    private List<string> CombineAllTagsWithoutRoot(List<string> tags)
    {
        if (tags == null || tags.Count == 0)
        {
            throw new OdinSystemException("Tags cannot be null or empty.");
        }

        var result = new List<string>(tags.Count);
        result.AddRange(BuildTags(tags));
        return result;
    }

    //


}
