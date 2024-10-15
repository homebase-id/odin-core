using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Threading;

namespace Odin.Core.Tests.Threading;

public class KeyedAsyncMutexTests
{
    [Test]
    public async Task LockedExecuteAsync_SingleExecution_CompletesSuccessfully()
    {
        var keyedMutex = new KeyedAsyncMutex();
        var executed = false;

        using (await keyedMutex.LockedExecuteAsync("test-key"))
        {
            Assert.AreEqual(1, keyedMutex.Count);
            executed = true;
            await Task.Delay(100);
        }

        Assert.IsTrue(executed);
        Assert.AreEqual(0, keyedMutex.Count);
    }

    [Test]
    public async Task LockedExecuteAsync_ConcurrentSameKey_ExecutesSerially()
    {
        var keyedMutex = new KeyedAsyncMutex();
        var counter = 0;

        async Task Action()
        {
            using (await keyedMutex.LockedExecuteAsync("test-key"))
            {
                await Task.Delay(100);
                counter++;
                Assert.AreEqual(1, keyedMutex.Count);
            }
        }

        await Action();
        await Action();
        await Action();

        // Since the actions use the same key, they should run serially, not concurrently.
        Assert.AreEqual(3, counter);
        Assert.AreEqual(0, keyedMutex.Count);
    }

    [Test]
    public async Task LockedExecuteAsync_ConcurrentDifferentKeys_ExecutesConcurrently()
    {
        var keyedMutex = new KeyedAsyncMutex();
        var runningTasks = new List<Task>();
        var counter = 0;

        // Set up tasks to execute concurrently with different keys
        for (int i = 0; i < 50; i++)
        {
            var key = $"key-{i}";
            runningTasks.Add(Task.Run(async () =>
            {
                using (await keyedMutex.LockedExecuteAsync(key))
                {
                    Interlocked.Increment(ref counter);
                    await Task.Delay(100);
                }
            }));
        }

        var allTasks = Task.WhenAll(runningTasks);

        var completed = await Task.WhenAny(allTasks, Task.Delay(150));

        Assert.AreEqual(50, counter);
        Assert.AreEqual(allTasks, completed, "Actions with different keys should execute concurrently.");
        Assert.AreEqual(0, keyedMutex.Count, "The count should be zero after all actions complete.");
    }
}
