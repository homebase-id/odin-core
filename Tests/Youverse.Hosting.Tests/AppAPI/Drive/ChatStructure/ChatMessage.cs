using System;

namespace Youverse.Hosting.Tests.AppAPI.Drive.ChatStructure;

public class ChatMessage
{
    public string Message { get; set; }
}

public class ChatGroupMessage
{
    public Guid GroupId { get; set; }
    public ChatMessage ChatMessage { get; set; }
}