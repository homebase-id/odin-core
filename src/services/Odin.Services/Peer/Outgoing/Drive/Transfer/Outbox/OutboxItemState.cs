using System.Collections.Generic;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer.Encryption;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

public class OutboxItemState
{
    public string Recipient { get; set; }

    public List<TransferAttempt> Attempts { get; }

    /// <summary>
    /// Indicates the file should be read from the temp folder of the drive and deleted after it is sent to all recipients
    /// </summary>
    public bool IsTransientFile { get; set; }
    
    public EncryptedRecipientTransferInstructionSet TransferInstructionSet { get; set; }

    /// <summary>
    /// TransitOptions provided when the file was sent by the client
    /// </summary>
    public TransitOptions OriginalTransitOptions { get; set; }

    /// <summary>
    /// Client Auth Token from the <see cref="IdentityConnectionRegistration"/> used to send the file to the recipient
    /// </summary>
    public byte[] EncryptedClientAuthToken { get; set; }
    
    public byte[] Data { get; set; }

    public T DeserializeData<T>()
    {
        return OdinSystemSerializer.Deserialize<T>(Data.ToStringFromUtf8Bytes());
    }
}