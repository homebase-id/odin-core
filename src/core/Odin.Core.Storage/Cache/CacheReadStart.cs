namespace Odin.Core.Storage.Cache;

/// <summary>
/// When a read started, from <see cref="IFusionCacheWrapper.BeginRead"/>, for a later
/// <see cref="IFusionCacheWrapper.SetIfNotRemovedSinceAsync{TValue}"/>
/// </summary>
public readonly record struct CacheReadStart(long Ticks);
