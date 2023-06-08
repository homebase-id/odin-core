using System;
using Odin.Core.Services.Contacts.Circle.Requests;

namespace Odin.Hosting.Controllers;

public class ConnectionRequestResponse : ConnectionRequestHeader
{
    public string SenderOdinId { get; set; }

    public Int64 ReceivedTimestampMilliseconds { get; set; }

    public static ConnectionRequestResponse FromConnectionRequest(ConnectionRequest arg)
    {
        return new ConnectionRequestResponse()
        {
            Id = arg.Id,
            ContactData = arg.ContactData,
            SenderOdinId = arg.SenderOdinId,
            CircleIds = arg.CircleIds,
            Message = arg.Message,
            ReceivedTimestampMilliseconds = arg.ReceivedTimestampMilliseconds,
            Recipient = arg.Recipient
        };
    }
}