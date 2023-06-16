using Odin.Core.Identity;
using Odin.Core.Services.EncryptionKeyService;

namespace Odin.Core.Services.Contacts.Circle.Requests;

public class PendingConnectionRequestHeader
{
    public OdinId SenderOdinId { get; set; }

    public long ReceivedTimestampMilliseconds { get; set; }

    public RsaEncryptedPayload Payload { get; set; }

    public PendingConnectionRequestHeader Redacted()
    {
        return new PendingConnectionRequestHeader()
        {
            SenderOdinId = this.SenderOdinId,
            ReceivedTimestampMilliseconds = this.ReceivedTimestampMilliseconds
            //no payload
        };
    }
}