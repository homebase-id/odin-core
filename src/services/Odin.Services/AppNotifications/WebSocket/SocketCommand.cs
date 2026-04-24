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
    CallInvite = 5001,
    CallOffer = 5002,
    CallAnswer = 5003,
    CallIce = 5004,
    CallHangup = 5005,
    CallReject = 5006,
    Whoami = 5007,
}
