using System;

namespace Odin.Services.Peer.Outgoing.Drive.Query;

public class RemoteFileExistsByGlobalTransitIdRequest
{
    public Guid DriveId { get; set; }
    public Guid GlobalTransitId { get; set; }
}
