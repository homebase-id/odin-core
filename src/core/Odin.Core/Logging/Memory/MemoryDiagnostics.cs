using System;
using Microsoft.Extensions.Logging;

namespace Odin.Core.Logging.Memory;

using System.Diagnostics;

public static class MemoryDiagnostics
{
    public static void LogMemoryBreakdown(ILogger logger)
    {
        var info = GC.GetGCMemoryInfo();
        var process = Process.GetCurrentProcess();

        var rss = process.WorkingSet64;
        var gcCommitted = info.TotalCommittedBytes;
        var heapSize = info.HeapSizeBytes;
        var fragmented = info.FragmentedBytes;
        var deadObjects = gcCommitted - heapSize - fragmented;
        var nativeMemory = rss - gcCommitted;

        logger.LogInformation(
            "Memory breakdown — RSS: {Rss} MB | GC committed: {GcCommitted} MB | " +
            "Live heap: {LiveHeap} MB | Dead (awaiting GC): {Dead} MB | " +
            "Fragmentation: {Frag} MB | Native: {Native} MB | " +
            "Gen0: {Gen0} | Gen1: {Gen1} | Gen2: {Gen2} | Pinned: {Pinned}",
            rss / 1_048_576,
            gcCommitted / 1_048_576,
            heapSize / 1_048_576,
            Math.Max(0, deadObjects) / 1_048_576,
            fragmented / 1_048_576,
            Math.Max(0, nativeMemory) / 1_048_576,
            info.GenerationInfo[0].SizeAfterBytes / 1_048_576,
            info.GenerationInfo[1].SizeAfterBytes / 1_048_576,
            info.GenerationInfo[2].SizeAfterBytes / 1_048_576,
            info.PinnedObjectsCount);
    }
}
