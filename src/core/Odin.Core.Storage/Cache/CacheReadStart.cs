namespace Odin.Core.Storage.Cache;

/// <summary>
/// The cache's remove count when a read started, from <see cref="IFusionCacheWrapper.BeginReadAsync"/>, for a later
/// <see cref="IFusionCacheWrapper.SetIfNotRemovedSinceAsync{TValue}"/>
/// </summary>
public readonly record struct CacheReadStart(long RemoveCount);
