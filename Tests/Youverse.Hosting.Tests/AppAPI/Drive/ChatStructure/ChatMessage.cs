using System;

namespace Youverse.Hosting.Tests.AppAPI.Drive.ChatStructure;

public class ChatMessage
{
    public const int FileType = 1010;

    public string Message { get; set; }
}

public class ChatGroupMessage
{
    public const int FileType = 1019;

    public Guid GroupId { get; set; }
    public ChatMessage ChatMessage { get; set; }
}