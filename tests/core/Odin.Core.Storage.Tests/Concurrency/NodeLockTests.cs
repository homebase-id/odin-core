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
    public async Task TryLockAsync_ShouldReturnNullWhenHeld()
    {
        var nodeLock = new NodeLock();

        await using (await nodeLock.LockAsync(NodeLockKey.Create("foo")))
        {
            var contended = await nodeLock.TryLockAsync(NodeLockKey.Create("foo"));
            Assert.That(contended, Is.Null, "TryLockAsync must not acquire a held lock");

            var otherKey = await nodeLock.TryLockAsync(NodeLockKey.Create("bar"));
            Assert.That(otherKey, Is.Not.Null, "TryLockAsync must not be blocked by an unrelated key");
            await otherKey!.DisposeAsync();
        }

        await using var acquired = await nodeLock.TryLockAsync(NodeLockKey.Create("foo"));
        Assert.That(acquired, Is.Not.Null, "TryLockAsync must acquire once the lock is released");
    }

}