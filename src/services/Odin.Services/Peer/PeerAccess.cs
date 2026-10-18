namespace Odin.Services.Peer;

/// <summary>
/// What an ordinary file operation does on a peer, which decides the transit permission it needs. Only the plain file
/// operations use it; reactions, comments, introductions, connections, read receipts and the like keep their own rules.
/// </summary>
public enum PeerAccess
{
    /// <summary>
    /// Gets or queries a peer's files: UseTransitRead. A write-only grant may never read.
    /// </summary>
    Read,

    /// <summary>
    /// Sends, updates or deletes files on a peer: UseTransitWrite. Read alone never writes.
    /// </summary>
    Write
}
