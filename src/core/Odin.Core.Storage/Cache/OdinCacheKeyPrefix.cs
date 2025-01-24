namespace Odin.Core.Storage.Cache;

#nullable enable

public class OdinCacheKeyPrefix(string prefix)
{
    public string Prefix { get; } = prefix + ":";

    public static implicit operator string(OdinCacheKeyPrefix odinCacheKeyPrefix)
    {
        return odinCacheKeyPrefix.Prefix;
    }
}
