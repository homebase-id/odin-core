using System;

namespace Odin.Services.Peer.Outgoing.Drive.Query;

public class FileExistsOnPeerResponse
{
    public bool Exists { get; set; }

    public Guid? VersionTag { get; set; }
}
