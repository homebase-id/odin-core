using System;
using Youverse.Core.Services.Contacts.Circle.Requests;

namespace Youverse.Hosting.Controllers;

public class ConnectionRequestResponse : ConnectionRequestHeader
{
    public string SenderDotYouId { get; set; }

    public string Name { get; set; }

    public Int64 ReceivedTimestampMilliseconds { get; set; }

    public static ConnectionRequestResponse FromConnectionRequest(ConnectionRequest arg)
    {
        return new ConnectionRequestResponse()
        {
            Id = arg.Id,
            Name = arg.Name,
            SenderDotYouId = arg.SenderDotYouId,
            CircleIds = arg.CircleIds,
            Message = arg.Message,
            ReceivedTimestampMilliseconds = arg.ReceivedTimestampMilliseconds,
            Recipient = arg.Recipient
        };
    }
}