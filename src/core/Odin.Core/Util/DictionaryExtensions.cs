using System.Collections.Generic;

namespace Odin.Core.Util;

#nullable enable

public static class DictionaryExtensions
{
    public static TValue? GetOrDefault<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key, TValue? defaultValue)
    {
        return dictionary.TryGetValue(key, out var value) ? value : defaultValue;
    }

    //

    public static TValue UpdateExisting<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key, TValue value)
    {
        var oldValue = dictionary[key];
        dictionary[key] = value;
        return oldValue;
    }

    //
}
