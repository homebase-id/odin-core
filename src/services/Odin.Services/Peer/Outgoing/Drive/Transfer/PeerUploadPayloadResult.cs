using System.Collections.Generic;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer;

public class PeerUploadPayloadResult
{
    public Dictionary<string, OutboxEnqueuingStatus> RecipientStatus { get; set; } = new();
}