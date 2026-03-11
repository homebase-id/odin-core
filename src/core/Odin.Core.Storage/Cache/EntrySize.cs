using System;

namespace Odin.Core.Storage.Cache;

public static class EntrySize
{
    // These are conservative guesstimates of the average size of small, medium, and large cache entries in bytes.
    // Before making these smaller, remember that the actual managed heap cost of an object is typically 3-5x
    // its serialized size due to object headers, string allocations, padding, and FusionCache wrapper overhead.
    public const long Small =   1 * BaseUnitBytes;    // guesstimated size: 1 KB
    public const long Medium = 10 * BaseUnitBytes;    // guesstimated size: 10 KB
    public const long Large = 100 * BaseUnitBytes;    // guesstimated size: 100 KB

    private const long BaseUnitBytes = 1024;

    public static long GuesstimateMemoryCacheSizeLimit()
    {
        var availableMemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        return availableMemoryBytes / 2;
    }
}
