using System;
using Microsoft.Extensions.Logging;

namespace Odin.Core.Logging.Memory;

using System.Diagnostics;

public class MemoryDiagnostics(ILogger<MemoryDiagnostics> logger)
{
    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;

    public void LogMemoryBreakdown()
    {
        var uptime = DateTimeOffset.UtcNow - _startTime;

        var info = GC.GetGCMemoryInfo();
        var process = Process.GetCurrentProcess();

        var rss = process.WorkingSet64;
        var gcCommitted = info.TotalCommittedBytes;
        var heapSize = info.HeapSizeBytes;
        var fragmented = info.FragmentedBytes;
        var deadObjects = gcCommitted - heapSize - fragmented;
        var nativeMemory = rss - gcCommitted;

        logger.LogInformation(
            "Memory breakdown uptime={uptime}m - RSS: {Rss} MB | GC committed: {GcCommitted} MB | " +
            "Live heap: {LiveHeap} MB | Dead (awaiting GC): {Dead} MB | " +
            "Fragmentation: {Frag} MB | Native: {Native} MB | " +
            "Gen0: {Gen0} | Gen1: {Gen1} | Gen2: {Gen2} | Pinned: {Pinned}",
            (int)uptime.TotalMinutes,
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
