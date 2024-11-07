using System;
using System.Threading.Tasks;
using Odin.Core.Serialization;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Apps;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests._Universal.Outbox.Performance;

public class ReadReceiptSocketHandler(int processInboxBatchSize, int notificationBatchSize, int notificationWaitTime)
{
    private readonly TestWebSocketListener _socketListener = new();

    private OwnerApiClientRedux _client;
    public event EventHandler<(TargetDrive targetDrive, SharedSecretEncryptedFileHeader header)> FileAdded;
    public event EventHandler<(TargetDrive targetDrive, SharedSecretEncryptedFileHeader header)> FileModified;

    public async Task ConnectAsync(OwnerApiClientRedux client)
    {
        this._client = client;

        _socketListener.NotificationReceived += SocketListenerOnNotificationReceived;
        await _socketListener.ConnectAsync(_client.Identity.OdinId, _client.GetTokenContext(), new EstablishConnectionOptions()
        {
            Drives = [SystemDriveConstants.ChatDrive],
            BatchSize = notificationBatchSize,
            WaitTimeMs = notificationWaitTime
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
                await _client.DriveRedux.ProcessInbox(SystemDriveConstants.ChatDrive, processInboxBatchSize);
                // Console.WriteLine($"Identity: {_client.Identity.OdinId}. Notification: InboxItemReceived");
                break;

            case ClientNotificationType.FileAdded:
                HandleFileAdded(notification);
                break;

            case ClientNotificationType.FileModified:
                HandleFileModified(notification);
                break;
        }
    }

    private void HandleFileModified(TestClientNotification notification)
    {
        var driveNotification = OdinSystemSerializer.Deserialize<ClientDriveNotification>(notification.Data);
        this.FileModified?.Invoke(this, (driveNotification.TargetDrive, driveNotification.Header));
    }

    private void HandleFileAdded(TestClientNotification notification)
    {
        var driveNotification = OdinSystemSerializer.Deserialize<ClientDriveNotification>(notification.Data);
        this.FileAdded?.Invoke(this, (driveNotification.TargetDrive, driveNotification.Header));
    }
}