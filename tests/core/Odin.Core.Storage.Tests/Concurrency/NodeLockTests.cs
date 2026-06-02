using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Storage.Concurrency;

namespace Odin.Core.Storage.Tests.Concurrency;

public class NodeLockTests
{
    [Test]
    public async Task ItShouldReleaseLockUsingAsyncDispose()
    {
        var nodeLock = new NodeLock();
        await using (await nodeLock.LockAsync(NodeLockKey.Create("foo")))
        {
            // Lock is acquired
        }

        // Lock is released

        await using (await nodeLock.LockAsync(NodeLockKey.Create("foo")))
        {
            // Lock is acquired
        }

        Assert.Pass();
    }

    [Test]
    public async Task TryRunWithLockAsync_RunsAction_AndReturnsTrue_WhenFree()
    {
        var nodeLock = new NodeLock();
        var ran = false;

        var result = await nodeLock.TryRunWithLockAsync(NodeLockKey.Create("foo"), () =>
        {
            ran = true;
            return Task.CompletedTask;
        });

        Assert.That(result, Is.True, "Should acquire an uncontended lock and run the action.");
        Assert.That(ran, Is.True);
    }

    [Test]
    public async Task TryRunWithLockAsync_SkipsAction_AndReturnsFalse_WhenHeld()
    {
        var nodeLock = new NodeLock();
        var key = NodeLockKey.Create("foo");

        await using (await nodeLock.LockAsync(key))
        {
            var ran = false;
            var result = await nodeLock.TryRunWithLockAsync(key, () =>
            {
                ran = true;
                return Task.CompletedTask;
            });

            Assert.That(result, Is.False, "Should not acquire a lock that is already held.");
            Assert.That(ran, Is.False, "Action must not run when the lock could not be acquired.");
        }
    }

    [Test]
    public async Task TryRunWithLockAsync_ConcurrentSameKey_OnlyOneRuns()
    {
        var nodeLock = new NodeLock();
        var key = NodeLockKey.Create("foo");
        var firstInside = new TaskCompletionSource();
        var release = new TaskCompletionSource();

        var first = nodeLock.TryRunWithLockAsync(key, async () =>
        {
            firstInside.SetResult();
            await release.Task;
        });

        await firstInside.Task; // first holds the lock and is inside its action

        var secondRan = false;
        var secondResult = await nodeLock.TryRunWithLockAsync(key, () =>
        {
            secondRan = true;
            return Task.CompletedTask;
        });

        Assert.That(secondResult, Is.False, "Second attempt must skip while the first holds the lock.");
        Assert.That(secondRan, Is.False);

        release.SetResult();
        Assert.That(await first, Is.True);
    }

    [Test]
    public async Task TryRunWithLockAsync_SucceedsAgain_AfterRelease()
    {
        var nodeLock = new NodeLock();
        var key = NodeLockKey.Create("foo");

        Assert.That(await nodeLock.TryRunWithLockAsync(key, () => Task.CompletedTask), Is.True);
        // Released -> can run again.
        Assert.That(await nodeLock.TryRunWithLockAsync(key, () => Task.CompletedTask), Is.True);
    }

    [Test]
    public async Task TryRunWithLockAsync_DifferentKeys_DoNotContend()
    {
        var nodeLock = new NodeLock();
        var aInside = new TaskCompletionSource();
        var release = new TaskCompletionSource();

        // Hold key "a" open, then "b" must still run concurrently.
        var a = nodeLock.TryRunWithLockAsync(NodeLockKey.Create("a"), async () =>
        {
            aInside.SetResult();
            await release.Task;
        });

        await aInside.Task;

        var bResult = await nodeLock.TryRunWithLockAsync(NodeLockKey.Create("b"), () => Task.CompletedTask);
        Assert.That(bResult, Is.True, "Distinct keys must not block each other.");

        release.SetResult();
        Assert.That(await a, Is.True);
    }

    [Test]
    public async Task TryRunWithLockAsync_ActionThrows_PropagatesAndReleasesLock()
    {
        var nodeLock = new NodeLock();
        var key = NodeLockKey.Create("foo");

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await nodeLock.TryRunWithLockAsync(key, () => throw new InvalidOperationException("boom")));

        // Lock must have been released despite the throw.
        Assert.That(await nodeLock.TryRunWithLockAsync(key, () => Task.CompletedTask), Is.True,
            "Lock should be released even when the action throws.");
    }

    [Test]
    public void TryRunWithLockAsync_CanceledToken_Throws()
    {
        var nodeLock = new NodeLock();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var ran = false;
        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await nodeLock.TryRunWithLockAsync(NodeLockKey.Create("foo"), () =>
            {
                ran = true;
                return Task.CompletedTask;
            }, cancellationToken: cts.Token));

        Assert.That(ran, Is.False);
    }
}