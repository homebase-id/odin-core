using System;
using System.Net.WebSockets;

namespace Odin.Services.AppNotifications.WebSocket;

public class CloseWebSocketException : OperationCanceledException
{
    public CloseWebSocketException(string message = "Closing WebSocket") : base(message)
    {
    }

    public CloseWebSocketException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Ends the socket with this close status and reason, for a handler that passes them to the close
    /// </summary>
    public CloseWebSocketException(WebSocketCloseStatus status, string reason) : base(reason)
    {
        Status = status;
        Reason = reason;
    }

    public WebSocketCloseStatus Status { get; } = WebSocketCloseStatus.NormalClosure;

    public string Reason { get; } = "";
}
