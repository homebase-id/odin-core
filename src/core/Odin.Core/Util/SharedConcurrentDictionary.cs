using System.Collections.Concurrent;

namespace Odin.Core.Util;

public class SharedConcurrentDictionary<TRegisteredService, TKey, TValue> : ConcurrentDictionary<TKey, TValue>
    where TRegisteredService : notnull
    where TKey : notnull
    where TValue : notnull
{
}
