namespace Youverse.Core.Services.AppNotifications.ClientNotifications
{
    public class ClientConnected : IOwnerConsoleNotification
    {
        public string Key => "ClientConnected";

        public string SocketId { get; set; }
    }
}