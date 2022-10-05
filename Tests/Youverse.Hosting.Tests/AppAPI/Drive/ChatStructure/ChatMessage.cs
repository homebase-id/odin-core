using System;

namespace Youverse.Hosting.Tests.AppAPI.Drive.ChatStructure;

public class ChatMessage
{
    public const int FileType = 1010;
    public string Sender { get; set; }
    public Guid GroupId { get; set; }
    public string Message { get; set; }
    public Guid Id { get; set; }
}