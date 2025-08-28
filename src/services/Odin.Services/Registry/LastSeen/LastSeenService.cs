using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Odin.Core.Time;

namespace Odin.Services.Registry.LastSeen;

#nullable enable

public interface ILastSeenService
{
    Dictionary<Guid, UnixTimeUtc> All { get; }
    void LastSeenNow(Guid identityId);
    UnixTimeUtc? GetLastSeen(Guid identityId);
    void PutLastSeen(Guid identityId, UnixTimeUtc lastSeen);
}

//

public class LastSeenService : ILastSeenService
{
    private readonly ConcurrentDictionary<Guid, UnixTimeUtc> _lastSeen = new();
    public Dictionary<Guid, UnixTimeUtc> All => _lastSeen.ToDictionary();

    //

    public void LastSeenNow(Guid identityId)
    {
        _lastSeen[identityId] = UnixTimeUtc.Now();
    }

    //

    public UnixTimeUtc? GetLastSeen(Guid identityId)
    {
        if (_lastSeen.TryGetValue(identityId, out var lastSeen))
        {
            return lastSeen;
        }

        return null;
    }

    //

    public void PutLastSeen(Guid identityId, UnixTimeUtc lastSeen)
    {
        _lastSeen[identityId] = lastSeen;
    }

    //

}
