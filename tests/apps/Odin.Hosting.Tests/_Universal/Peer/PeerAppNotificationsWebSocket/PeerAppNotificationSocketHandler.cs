using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Hosting.Tests._Universal.ApiClient;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests._Universal.Peer.PeerAppNotificationsWebSocket;

public class PeerAppNotificationSocketHandler()
{
    private readonly PeerTestAppWebSocketListener _socketListener = new();
    public event EventHandler<(TargetDrive targetDrive, SharedSecretEncryptedFileHeader header)> FileAdded;
    public event EventHandler<(TargetDrive targetDrive, SharedSecretEncryptedFileHeader header)> FileModified;

    public async Task ConnectAsync(OdinId hostIdentity, ClientAccessToken token, List<TargetDrive> targetDrives)
    {
        _socketListener.NotificationReceived += SocketListenerOnNotificationReceived;

        // negotiate with the host identity, we need to send a toke

        await _socketListener.ConnectAsync(hostIdentity, token, new EstablishConnectionOptions()
        {
            Drives = targetDrives,
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
            case ClientNotificationType.FileAdded:
                await HandleFileAdded(notification);
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

    private async Task HandleFileAdded(TestClientNotification notification)
    {
        var driveNotification = OdinSystemSerializer.Deserialize<ClientDriveNotification>(notification.Data);
        this.FileAdded?.Invoke(this, (driveNotification.TargetDrive, driveNotification.Header));
        await Task.CompletedTask;
    }
}