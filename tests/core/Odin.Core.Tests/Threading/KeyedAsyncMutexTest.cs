using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Threading;

namespace Odin.Core.Tests.Threading;

[TestFixture]
public class KeyedAsyncMutexTests
{
    [Test]
    public async Task ExecuteAsync_SingleExecution_CompletesSuccessfully()
    {
        var keyedMutex = new KeyedAsyncMutex();
        var executed = false;

        await keyedMutex.LockedExecuteAsync("test-key", async () =>
        {
            Assert.AreEqual(1, keyedMutex.Count);
            executed = true;
            await Task.Delay(100);
        });

        Assert.IsTrue(executed, "The action should have been executed successfully.");
        Assert.AreEqual(0, keyedMutex.Count, "The count should be zero after the action completes.");
    }

    [Test]
    public async Task ExecuteAsync_ConcurrentSameKey_ExecutesSerially()
    {
        var keyedMutex = new KeyedAsyncMutex();
        var counter = 0;

        async Task Action()
        {
            Assert.AreEqual(1, keyedMutex.Count);
            var current = counter;
            await Task.Delay(100); // Simulate some work
            counter = current + 1;
        }

        var task1 = keyedMutex.LockedExecuteAsync("test-key", Action);
        var task2 = keyedMutex.LockedExecuteAsync("test-key", Action);

        await Task.WhenAll(task1, task2);

        // Since the actions use the same key, they should run serially, not concurrently.
        Assert.AreEqual(2, counter, "Actions with the same key should execute serially.");
        Assert.AreEqual(0, keyedMutex.Count, "The count should be zero after both actions complete.");
    }

    [Test]
    public async Task ExecuteAsync_ConcurrentDifferentKeys_ExecutesConcurrently()
    {
        var keyedMutex = new KeyedAsyncMutex();
        var runningTasks = new List<Task>();
        var counter = 0;

        // Set up tasks to execute concurrently with different keys
        for (int i = 0; i < 10; i++)
        {
            var key = $"key-{i}";
            runningTasks.Add(keyedMutex.LockedExecuteAsync(key, async () =>
            {
                Interlocked.Increment(ref counter);
                Assert.That(keyedMutex.Count, Is.EqualTo(counter));
                await Task.Delay(100); // Simulate some work
            }));
        }

        var allTasks = Task.WhenAll(runningTasks);
        var completed = await Task.WhenAny(allTasks, Task.Delay(200));

        // All tasks should complete within the delay since they are using different keys.
        Assert.AreEqual(allTasks, completed, "Actions with different keys should execute concurrently.");
        Assert.AreEqual(0, keyedMutex.Count, "The count should be zero after all actions complete.");
        Assert.That(counter, Is.EqualTo(10));
    }

    [Test]
    public async Task ExecuteAsync_ReferenceCount_DecrementsCorrectly()
    {
        var keyedMutex = new KeyedAsyncMutex();
        var executionCount = 0;

        async Task Action()
        {
            await Task.Delay(50); // Simulate some work
            executionCount++;
        }

        // Execute twice with the same key to increment the reference count
        await keyedMutex.LockedExecuteAsync("test-key", Action);
        await keyedMutex.LockedExecuteAsync("test-key", Action);

        // Execute again to see if the key is correctly removed afterward
        await keyedMutex.LockedExecuteAsync("test-key", Action);

        Assert.AreEqual(3, executionCount, "The action should have been executed three times.");
        Assert.AreEqual(0, keyedMutex.Count, "The mutex for 'test-key' should have been removed after the final execution.");
    }
}
