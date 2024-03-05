using Odin.Core.Identity;
using Odin.Core.Services.EncryptionKeyService;
using Odin.Core.Time;

namespace Odin.Core.Services.Membership.Connections.Requests;

public class PendingConnectionRequestHeader
{
    public OdinId SenderOdinId { get; set; }

    public UnixTimeUtc ReceivedTimestampMilliseconds { get; set; }

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