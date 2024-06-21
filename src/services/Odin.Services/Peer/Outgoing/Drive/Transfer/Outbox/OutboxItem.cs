using System;
using Odin.Core.Identity;
using Odin.Services.Drives;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

public class OutboxItem
{
    public OdinId Recipient { get; set; }
    public InternalDriveFileId File { get; set; }
    public int Priority { get; set; }

    public Guid Marker { get; set; }

    public bool IsTransientFile { get; set; }
    public byte[] EncryptedClientAuthToken { get; set; }
    public OutboxItemType Type { get; set; }
    public int AttemptCount { get; set; }

    public string Data { get; set; }

    public Guid? DependencyFileId { get; set; }
    public long Created { get; set; }
}