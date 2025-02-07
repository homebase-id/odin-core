namespace Odin.Services.AppNotifications.WebSocket;

public class SocketCommand
{
    public SocketCommandType Command { get; set; }

    public string Data { get; set; }
}

public enum SocketCommandType
{
    EstablishConnectionRequest = 001,
    ProcessTransitInstructions = 111,
    ProcessInbox = 222,
    Ping = 999,
    WhoIsOnline = 444,
}
