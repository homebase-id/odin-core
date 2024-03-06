using System.Diagnostics;

namespace Odin.Test.Helpers.Benchmark;

public static class TestBenchmark
{
    public static void StopWatchStatus(string s, Stopwatch stopWatch)
    {
        TimeSpan ts = stopWatch.Elapsed;

        // Format and display the TimeSpan value.
        string elapsedTime = String.Format("{0:00}:{1:00}:{2:00}.{3:00}",
            ts.Hours, ts.Minutes, ts.Seconds,
            ts.Milliseconds / 10);

        Console.WriteLine(elapsedTime + " : " + s);
        stopWatch.Reset();
    }
}
