using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests._Universal.Outbox.Performance;

public class ReadReceiptSocketHandler
{
    private readonly TestWebSocketListener _socketListener = new();

    private OwnerApiClientRedux _client;

    public async Task ConnectAsync(OwnerApiClientRedux client)
    {
        this._client = client;

        _socketListener.NotificationReceived += SocketListenerOnNotificationReceived;
        await _socketListener.ConnectAsync(_client.Identity.OdinId, _client.GetTokenContext(), new EstablishConnectionOptions()
        {
            Drives = [SystemDriveConstants.ChatDrive],
            BatchSize = 1,
            WaitTimeMs = 1
        });
    }

    public async Task DisconnectAsync()
    {
        await _socketListener.DisconnectAsync();
    }

    private async Task SocketListenerOnNotificationReceived(TestClientNotification notification)
    {
        switch (notification.NotificationType)
        {
            case ClientNotificationType.InboxItemReceived:
                await this.ProcessInbox();
                break;
            
            case ClientNotificationType.FileAdded:
                break;
            
            case ClientNotificationType.FileModified:
                break;
            
            case ClientNotificationType.FileDeleted:
                break;
        }
    }

    private async Task ProcessInbox()
    {
        await _client.DriveRedux.ProcessInbox(SystemDriveConstants.ChatDrive, 2000);
    }
}