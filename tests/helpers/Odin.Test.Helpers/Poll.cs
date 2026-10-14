using System.Diagnostics;
using NUnit.Framework;

namespace Odin.Test.Helpers;

public static class Poll
{
    /// <summary>
    /// Waits for background work to show its effect. A fixed sleep before the assert is too short on a
    /// loaded CI runner and wasted time everywhere else.
    /// </summary>
    public static async Task UntilAsync(Func<Task<bool>> condition, TimeSpan maxWait, Func<string> describe)
    {
        var sw = Stopwatch.StartNew();
        while (!await condition())
        {
            if (sw.Elapsed > maxWait)
            {
                Assert.Fail($"After {maxWait}: {describe()}");
            }
            await Task.Delay(50);
        }
    }
}
