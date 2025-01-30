namespace Odin.Core.Storage.Cache;

#nullable enable

public class CacheKeyPrefix(string prefix)
{
    public string Prefix { get; } = prefix + ":";

    public static implicit operator string(CacheKeyPrefix cacheKeyPrefix)
    {
        return cacheKeyPrefix.Prefix;
    }
}
