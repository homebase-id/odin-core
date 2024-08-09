using Odin.Services.Membership.Connections.Requests;
using Odin.Core.Time;

namespace Odin.Hosting.Controllers;

public class ConnectionRequestResponse : ConnectionRequestHeader
{
    public string SenderOdinId { get; set; }

    public UnixTimeUtc ReceivedTimestampMilliseconds { get; set; }

    public ConnectionRequestDirection Direction { get; set; }

    public static ConnectionRequestResponse FromConnectionRequest(ConnectionRequest arg, ConnectionRequestDirection direction)
    {
        return new ConnectionRequestResponse()
        {
            // Id = arg.Id,
            ContactData = arg.ContactData,
            SenderOdinId = arg.SenderOdinId,
            CircleIds = arg.CircleIds,
            Message = arg.Message,
            IntroducerOdinId = arg.IntroducerOdinId,
            ReceivedTimestampMilliseconds = arg.ReceivedTimestampMilliseconds,
            ConnectionRequestOrigin = arg.ConnectionRequestOrigin,
            Recipient = arg.Recipient,
            Direction = direction
        };
    }
}