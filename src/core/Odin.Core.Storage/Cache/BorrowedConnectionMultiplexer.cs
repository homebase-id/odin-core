using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace Odin.Core.Storage.Cache;

#nullable enable

/// <summary>
/// The container's shared <see cref="IConnectionMultiplexer"/>, lent to a component that disposes the connection it is
/// given.  FusionCache's Redis backplane closes and disposes its multiplexer when it unsubscribes (on a thread-pool
/// thread, as the cache is disposed), while the container disposes the same singleton: two closes of one multiplexer at
/// once can throw inside StackExchange.Redis (#2069).  Every call goes through except the close and dispose ones,
/// which are the owner's.
/// </summary>
public class BorrowedConnectionMultiplexer : DispatchProxy
{
    private IConnectionMultiplexer _owned = null!;

    public static IConnectionMultiplexer Lend(IConnectionMultiplexer owned)
    {
        var borrowed = Create<IConnectionMultiplexer, BorrowedConnectionMultiplexer>();
        ((BorrowedConnectionMultiplexer)(object)borrowed)._owned = owned;
        return borrowed;
    }

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        switch (method!.Name)
        {
            case nameof(IConnectionMultiplexer.Close):
            case nameof(IDisposable.Dispose):
                return null;
            case nameof(IConnectionMultiplexer.CloseAsync):
                return Task.CompletedTask;
            case nameof(IAsyncDisposable.DisposeAsync):
                return ValueTask.CompletedTask;
        }

        try
        {
            return method.Invoke(_owned, args);
        }
        catch (TargetInvocationException e) when (e.InnerException != null)
        {
            // The caller handles the multiplexer's own exceptions (a connection failure, say), not reflection's wrapper
            ExceptionDispatchInfo.Throw(e.InnerException);
            throw;
        }
    }
}
