using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Threading;

namespace Odin.Core.Tests.Threading;

public class KeyedAsyncLockTests
{
    [Test]
    public async Task LockedExecuteAsync_SingleKey_AllowsOnlyOneAtATime()
    {
        var keyedMutex = new KeyedAsyncLock();
        var key = "testKey";
        var executionOrder = new List<int>();
        var task1Started = new TaskCompletionSource<bool>();
        var task2Started = new TaskCompletionSource<bool>();

        var task1 = Task.Run(async () =>
        {
            using (await keyedMutex.LockAsync(key))
            {
                executionOrder.Add(1);
                task1Started.SetResult(true);
                // Wait to ensure task2 tries to acquire the lock
                await Task.Delay(100);
            }
        });

        var task2 = Task.Run(async () =>
        {
            await task1Started.Task;
            using (await keyedMutex.LockAsync(key))
            {
                executionOrder.Add(2);
                task2Started.SetResult(true);
            }
        });

        await Task.WhenAll(task1, task2);

        Assert.That(executionOrder, Is.EqualTo(new List<int> { 1, 2 }));
    }

    [Test]
    public async Task LockedExecuteAsync_DifferentKeys_DoNotBlockEachOther()
    {
        var keyedMutex = new KeyedAsyncLock();
        var key1 = "key1";
        var key2 = "key2";
        var tasksCompleted = 0;

        var task1 = Task.Run(async () =>
        {
            using (await keyedMutex.LockAsync(key1))
            {
                // Simulate work
                await Task.Delay(100);
                Interlocked.Increment(ref tasksCompleted);
            }
        });

        var task2 = Task.Run(async () =>
        {
            using (await keyedMutex.LockAsync(key2))
            {
                // Simulate work
                await Task.Delay(100);
                Interlocked.Increment(ref tasksCompleted);
            }
        });

        await Task.WhenAll(task1, task2);

        Assert.That(tasksCompleted, Is.EqualTo(2));
    }

    [Test]
    public async Task LockedExecuteAsync_LockReleased_MutexCountDecreases()
    {
        var keyedMutex = new KeyedAsyncLock();
        var key = "testKey";

        Assert.That(keyedMutex.Count, Is.EqualTo(0));

        var disposer = await keyedMutex.LockAsync(key);

        Assert.That(keyedMutex.Count, Is.EqualTo(1));

        disposer.Dispose();

        Assert.That(keyedMutex.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task LockedExecuteAsync_ExceptionWithinLock_LockIsReleased()
    {
        var keyedMutex = new KeyedAsyncLock();
        var key = "testKey";

        try
        {
            using (await keyedMutex.LockAsync(key))
            {
                throw new InvalidOperationException("Test exception");
            }
        }
        catch (InvalidOperationException)
        {
            // Expected exception
        }

        Assert.That(keyedMutex.Count, Is.EqualTo(0));

        // Ensure another task can acquire the lock
        var wasAcquired = false;
        using (await keyedMutex.LockAsync(key))
        {
            wasAcquired = true;
        }

        Assert.IsTrue(wasAcquired);
    }

    [Test]
    public async Task LockedExecuteAsync_MultipleTasksSameKey_TasksAreSynchronized()
    {
        var keyedMutex = new KeyedAsyncLock();
        var key = "sharedKey";
        var runningTasks = 0;
        var maxConcurrentTasks = 0;

        var tasks = new List<Task>();

        for (int i = 0; i < 5; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                using (await keyedMutex.LockAsync(key))
                {
                    Interlocked.Increment(ref runningTasks);
                    maxConcurrentTasks = Math.Max(maxConcurrentTasks, runningTasks);
                    // Simulate work
                    await Task.Delay(50);
                    Interlocked.Decrement(ref runningTasks);
                }
            }));
        }

        await Task.WhenAll(tasks);

        Assert.That(maxConcurrentTasks, Is.EqualTo(1));
    }

    [Test]
    public async Task LockedExecuteAsync_MultipleTasksDifferentKeys_TasksRunConcurrently()
    {
        var keyedMutex = new KeyedAsyncLock();
        var runningTasks = 0;
        var maxConcurrentTasks = 0;

        var tasks = new List<Task>();

        for (int i = 0; i < 5; i++)
        {
            var key = $"key{i}";
            tasks.Add(Task.Run(async () =>
            {
                using (await keyedMutex.LockAsync(key))
                {
                    Interlocked.Increment(ref runningTasks);
                    maxConcurrentTasks = Math.Max(maxConcurrentTasks, runningTasks);
                    // Simulate work
                    await Task.Delay(50);
                    Interlocked.Decrement(ref runningTasks);
                }
            }));
        }

        await Task.WhenAll(tasks);

        Assert.That(maxConcurrentTasks, Is.GreaterThan(1));
    }

    [Test]
    public async Task LockedExecuteAsync_ReentrantLocking_ThrowsException()
    {
        var keyedMutex = new KeyedAsyncLock();
        var key = "testKey";
        using (await keyedMutex.LockAsync(key))
        {
            // Attempt to acquire the same lock again on the same thread
            var lockTask = keyedMutex.LockAsync(key);
            var isCompleted = lockTask.IsCompleted;

            Assert.IsFalse(isCompleted, "Lock task should not be completed because the lock is already held.");

            // The lockTask will wait indefinitely; cancel it to avoid deadlock
            var cancellationTokenSource = new CancellationTokenSource(100);
            try
            {
                using (await lockTask.WithCancellation(cancellationTokenSource.Token))
                {
                    Assert.Fail("Reentrant locking should not be possible.");
                }
            }
            catch (OperationCanceledException)
            {
                // Expected due to cancellation
            }
        }
    }

    [Test]
    public async Task LockedExecuteAsync_MultipleLocks_CountIsAccurate()
    {
        var keyedMutex = new KeyedAsyncLock();
        var key1 = "key1";
        var key2 = "key2";
        var key3 = "key3";

        Assert.That(keyedMutex.Count, Is.EqualTo(0));

        var disposer1 = await keyedMutex.LockAsync(key1);
        var disposer2 = await keyedMutex.LockAsync(key2);

        Assert.That(keyedMutex.Count, Is.EqualTo(2));

        disposer1.Dispose();

        Assert.That(keyedMutex.Count, Is.EqualTo(1));

        var disposer3 = await keyedMutex.LockAsync(key3);

        Assert.That(keyedMutex.Count, Is.EqualTo(2));

        disposer2.Dispose();
        disposer3.Dispose();

        Assert.That(keyedMutex.Count, Is.EqualTo(0));
    }


    [Test]
    public async Task LockedExecuteAsync_SingleExecution_CompletesSuccessfully()
    {
        var keyedMutex = new KeyedAsyncLock();
        var executed = false;

        using (await keyedMutex.LockAsync("test-key"))
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
        var keyedMutex = new KeyedAsyncLock();
        var counter = 0;

        async Task Action()
        {
            using (await keyedMutex.LockAsync("test-key"))
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
    public async Task LockedExecuteAsync_ParallelSameKey_ExecutesSequentially()
    {
        var keyedMutex = new KeyedAsyncLock();
        var runningTasks = new List<Task>();
        var counter = 0;

        for (var i = 0; i < 1234; i++)
        {
            runningTasks.Add(Task.Run(async () =>
            {
                using (await keyedMutex.LockAsync("somekey"))
                {
                    Assert.AreEqual(1, keyedMutex.Count);
                    counter++;
                }
            }));
        }

        await Task.WhenAll(runningTasks);

        // Since the actions use the same key, they should run serially, not concurrently.
        Assert.AreEqual(1234, counter);
        Assert.AreEqual(0, keyedMutex.Count);
    }

    [Test]
    public async Task LockedExecuteAsync_ConcurrentDifferentKeys_ExecutesConcurrently()
    {
        var keyedMutex = new KeyedAsyncLock();
        var runningTasks = new List<Task>();
        var counter = 0;

        // Set up tasks to execute concurrently with different keys
        for (int i = 0; i < 50; i++)
        {
            var key = $"key-{i}";
            runningTasks.Add(Task.Run(async () =>
            {
                using (await keyedMutex.LockAsync(key))
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

    [Test]
    public async Task LockedExecute_MixSyncAndAsync_SerializesAccess()
    {
        var keyedMutex = new KeyedAsyncLock();
        var key = "mixedKey";
        var executionOrder = new List<int>();
        var task1Started = new TaskCompletionSource<bool>();
        var task2Started = new TaskCompletionSource<bool>();

        var task1 = Task.Run(async () =>
        {
            using (await keyedMutex.LockAsync(key))
            {
                executionOrder.Add(1);
                task1Started.SetResult(true);
                // Wait to ensure task2 tries to acquire the lock
                await Task.Delay(100);
            }
        });

        var task2 = Task.Run(() =>
        {
            // Wait for task1 to start and acquire the lock
            task1Started.Task.Wait();
            using (keyedMutex.Lock(key))
            {
                executionOrder.Add(2);
                task2Started.SetResult(true);
            }
        });

        await Task.WhenAll(task1, task2);

        Assert.That(executionOrder, Is.EqualTo(new List<int> { 1, 2 }));
    }

    [Test]
    public async Task LockedExecute_MixAsyncAndSync_SerializesAccess()
    {
        var keyedMutex = new KeyedAsyncLock();
        var key = "mixedKey2";
        var executionOrder = new List<int>();
        var task1Started = new TaskCompletionSource<bool>();
        var task2Started = new TaskCompletionSource<bool>();

        var task1 = Task.Run(() =>
        {
            using (keyedMutex.Lock(key))
            {
                executionOrder.Add(1);
                task1Started.SetResult(true);
                // Wait to ensure task2 tries to acquire the lock
                Thread.Sleep(100);
            }
        });

        var task2 = Task.Run(async () =>
        {
            // Wait for task1 to start and acquire the lock
            await task1Started.Task;
            using (await keyedMutex.LockAsync(key))
            {
                executionOrder.Add(2);
                task2Started.SetResult(true);
            }
        });

        await Task.WhenAll(task1, task2);

        Assert.That(executionOrder, Is.EqualTo(new List<int> { 1, 2 }));
    }

    [Test]
    public async Task LockedExecute_MixSyncAndAsync_ParallelExecution()
    {
        var keyedMutex = new KeyedAsyncLock();
        var key = "mixedKey3";
        var counter = 0;

        var task1 = Task.Run(async () =>
        {
            for (int i = 0; i < 10; i++)
            {
                using (await keyedMutex.LockAsync(key))
                {
                    counter++;
                    // Simulate work
                    await Task.Delay(10);
                }
            }
        });

        var task2 = Task.Run(() =>
        {
            for (int i = 0; i < 10; i++)
            {
                using (keyedMutex.Lock(key))
                {
                    counter++;
                    // Simulate work
                    Thread.Sleep(10);
                }
            }
        });

        await Task.WhenAll(task1, task2);

        // Since the tasks use the same key, they should execute serially, not concurrently.
        Assert.That(counter, Is.EqualTo(20));
    }
}

public static class TaskExtensions
{
    public static async Task<T> WithCancellation<T>(this Task<T> task, CancellationToken cancellationToken)
    {
        using (var tcs = new CancellationTokenTaskSource<bool>(cancellationToken))
        {
            var completedTask = await Task.WhenAny(task, tcs.Task);
            if (completedTask == tcs.Task)
                throw new OperationCanceledException(cancellationToken);

            return await task;
        }
    }


    private class CancellationTokenTaskSource<T> : IDisposable
    {
        private TaskCompletionSource<T> _tcs;
        private CancellationTokenRegistration _registration;

        public Task<T> Task => _tcs.Task;

        public CancellationTokenTaskSource(CancellationToken cancellationToken)
        {
            _tcs = new TaskCompletionSource<T>();
            _registration = cancellationToken.Register(() => _tcs.TrySetCanceled());
        }

        public void Dispose()
        {
            _registration.Dispose();
        }
    }
}