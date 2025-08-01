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

}