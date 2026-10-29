using System.Collections.Concurrent;
using Odin.Core.Time;
using Odin.Services.Drives;

namespace Odin.Services.Base;

/// <summary>
/// The modified time each file had after the last change an HTTP request made to it, recorded from the drive events
/// published with that request's context: the events a socket excluding its own writes leaves out. A write's response
/// answers it, so such a client learns its write's time without the event or a feed read. Changes made within the
/// request by others' context (inbox items, synced posts, reaction summaries) and by later work are not in it.
/// Shared by the request's context and every copy that is the same request with more permissions.
/// </summary>
public sealed class RequestOwnWrites
{
    private readonly ConcurrentDictionary<InternalDriveFileId, UnixTimeUtc> _updated = new();

    public void Record(InternalDriveFileId file, UnixTimeUtc updated) =>
        _updated.AddOrUpdate(file, updated, (_, seen) => updated.milliseconds > seen.milliseconds ? updated : seen);

    /// <summary>
    /// The later of <paramref name="written"/> (the time the caller's own header write returned) and any own change
    /// recorded after it, such as the transfer history an upload with recipients writes
    /// </summary>
    public UnixTimeUtc UpdatedAfter(InternalDriveFileId file, UnixTimeUtc written) =>
        _updated.TryGetValue(file, out var recorded) && recorded.milliseconds > written.milliseconds ? recorded : written;
}
