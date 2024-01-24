using System;

namespace Odin.Core.Services.AppNotifications.WebSocket;

public class CloseWebSocketException : OperationCanceledException
{
    public CloseWebSocketException(string message = "Closing WebSocket") : base(message)
    {
    }

    public CloseWebSocketException(string message, Exception innerException) : base(message, innerException)
    {
    }
}