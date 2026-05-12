using System;

namespace Odin.Services.Peer.Outgoing.Drive.Query;

public class RemoteFileExistsByUniqueIdRequest
{
    public Guid DriveId { get; set; }
    public Guid UniqueId { get; set; }
}
