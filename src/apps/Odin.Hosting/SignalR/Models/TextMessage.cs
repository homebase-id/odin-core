using System;

namespace Odin.Hosting.SignalR.Models;

public class TextMessage
{
    public DateTime Timestamp { get; set; }
    public string Message { get; set; } = "ping";
}
