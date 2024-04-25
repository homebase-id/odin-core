using System.Collections.Generic;
using Odin.Services.Peer.Encryption;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

public class OutboxItemState
{
    public string Recipient { get; set; }

    public List<TransferAttempt> Attempts { get; }

    public bool IsTransientFile { get; set; }
    public EncryptedRecipientTransferInstructionSet TransferInstructionSet { get; set; }

    public TransitOptions OriginalTransitOptions { get; set; }
    public byte[] EncryptedClientAuthToken { get; set; }
}