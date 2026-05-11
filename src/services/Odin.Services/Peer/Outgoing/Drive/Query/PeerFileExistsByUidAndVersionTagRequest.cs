using System;
using Odin.Core.Identity;

namespace Odin.Services.Peer.Outgoing.Drive.Query;

public class PeerFileExistsByUidAndVersionTagRequest
{
    public OdinId OdinId { get; set; }
    public Guid UniqueId { get; set; }
    public Guid VersionTag { get; set; }
}