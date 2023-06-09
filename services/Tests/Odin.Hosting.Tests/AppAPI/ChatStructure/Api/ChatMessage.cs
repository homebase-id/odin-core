using System;
using System.Collections.Generic;
using Odin.Core.Time;

namespace Odin.Hosting.Tests.AppAPI.ChatStructure.Api;

public class ChatMessage
{
    public const int FileType = 1010;

    public Guid Id { get; set; }

    public string Sender { get; set; }

    public Guid ConversationId { get; set; }

    public string Text { get; set; }
    
    public List<Reaction> Reactions { get; set; }
    
    public List<ReadReceipt> ReadReceipts { get; set; }

    public Int64 ReceivedTimestamp { get; set; }
}

public class Reaction
{
    public string Sender { get; set; }
    public string ReactionValue { get; set; }
    
    public UnixTimeUtc Timestamp { get; set; }

}

public class ReadReceipt
{
    public string Sender { get; set; }
    public UnixTimeUtc Timestamp { get; set; }
}