namespace Odin.Core.Storage.Cache;

#nullable enable

public class OdinCacheOptions
{
    public Level2CacheType Level2CacheType { get; init; }
    public string? Level2Configuration { get; init; } = "";
}
