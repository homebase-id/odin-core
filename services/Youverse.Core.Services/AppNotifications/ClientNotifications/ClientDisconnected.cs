namespace Youverse.Core.Services.AppNotifications.ClientNotifications
{
    public class ClientDisconnected :  IOwnerConsoleNotification
    {
        public string Key => "ClientDisconnected";
        public string SocketId { get; set; }
    }
}