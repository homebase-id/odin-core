using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Peer.Incoming.Drive.Transfer;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

namespace Odin.Hosting.Controllers.Base.Drive.Status;

public class DriveStatus
{
    public OutboxStatus Outbox { get; set; }
    public InboxStatus Inbox { get; set; }
    public DriveSizeInfo SizeInfo { get; set; }
}