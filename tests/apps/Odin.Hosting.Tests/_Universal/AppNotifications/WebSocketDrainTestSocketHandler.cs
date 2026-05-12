using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Serialization;
using Odin.Hosting.Tests._Universal.ApiClient;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Apps;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests._Universal.AppNotifications;

// Passive WS listener for verifying the server-side inbox drain triggered by
// InboxItemReceivedNotification. Unlike ReadReceiptSocketHandler, this handler
// deliberately does NOT call ProcessInbox on InboxItemReceived — the point of
// the tests is that the server now drains on its own.
public sealed class WebSocketDrainTestSocketHandler
{
    private readonly TestOwnerWebSocketListener _socketListener = new();
    private readonly ConcurrentBag<Guid> _fileAddedGlobalTransitIds = new();
    private readonly ConcurrentBag<(TargetDrive Drive, SharedSecretEncryptedFileHeader Header)> _fileAdded = new();
    private readonly ConcurrentBag<(TargetDrive Drive, SharedSecretEncryptedFileHeader Header)> _fileModified = new();
    private readonly ConcurrentBag<TargetDrive> _inboxItemReceived = new();
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<bool>> _waiters = new();

    public IReadOnlyCollection<(TargetDrive Drive, SharedSecretEncryptedFileHeader Header)> FileAddedEvents => _fileAdded;
    public IReadOnlyCollection<(TargetDrive Drive, SharedSecretEncryptedFileHeader Header)> FileModifiedEvents => _fileModified;
    public IReadOnlyCollection<TargetDrive> InboxItemReceivedEvents => _inboxItemReceived;

    public async Task ConnectAsync(OwnerApiClientRedux client, TargetDrive targetDrive)
    {
        _socketListener.NotificationReceived += OnNotificationReceived;
        await _socketListener.ConnectAsync(
            client.Identity.OdinId,
            client.GetTokenContext(),
            new EstablishConnectionOptions { Drives = [targetDrive] });
    }

    public Task DisconnectAsync() => _socketListener.DisconnectAsync();

    private Task OnNotificationReceived(TestClientNotification notification)
    {
        switch (notification.NotificationType)
        {
            case ClientNotificationType.InboxItemReceived:
                {
                    var payload = OdinSystemSerializer.Deserialize<InboxItemReceivedPayload>(notification.Data);
                    if (payload?.TargetDrive != null)
                    {
                        _inboxItemReceived.Add(payload.TargetDrive);
                    }
                    break;
                }
            case ClientNotificationType.FileAdded:
                {
                    var driveNotification = OdinSystemSerializer.Deserialize<ClientDriveNotification>(notification.Data);
                    if (driveNotification?.Header != null && driveNotification.TargetDrive != null)
                    {
                        _fileAdded.Add((driveNotification.TargetDrive, driveNotification.Header));
                        var gtid = driveNotification.Header.FileMetadata?.GlobalTransitId;
                        if (gtid.HasValue)
                        {
                            _fileAddedGlobalTransitIds.Add(gtid.Value);
                            if (_waiters.TryGetValue(gtid.Value, out var tcs))
                            {
                                tcs.TrySetResult(true);
                            }
                        }
                    }
                    break;
                }
            case ClientNotificationType.FileModified:
                {
                    var driveNotification = OdinSystemSerializer.Deserialize<ClientDriveNotification>(notification.Data);
                    if (driveNotification?.Header != null && driveNotification.TargetDrive != null)
                    {
                        _fileModified.Add((driveNotification.TargetDrive, driveNotification.Header));
                    }
                    break;
                }
        }

        return Task.CompletedTask;
    }

    public async Task<bool> WaitForFileAdded(Guid globalTransitId, TimeSpan timeout)
    {
        if (_fileAddedGlobalTransitIds.Contains(globalTransitId))
        {
            return true;
        }

        var tcs = _waiters.GetOrAdd(globalTransitId, _ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));

        // Re-check after registering — covers the race where the event arrived
        // between the contains-check and the waiter being inserted.
        if (_fileAddedGlobalTransitIds.Contains(globalTransitId))
        {
            tcs.TrySetResult(true);
            return true;
        }

        using var cts = new CancellationTokenSource(timeout);
        await using (cts.Token.Register(() => tcs.TrySetResult(false)))
        {
            return await tcs.Task;
        }
    }

    public async Task<bool> WaitForInboxItemReceived(TargetDrive targetDrive, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (_inboxItemReceived.Any(d => d.Alias == targetDrive.Alias && d.Type == targetDrive.Type))
            {
                return true;
            }
            await Task.Delay(50);
        }
        return false;
    }

    public async Task<bool> WaitForAllFilesAdded(IEnumerable<Guid> globalTransitIds, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        foreach (var gtid in globalTransitIds)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return false;
            }

            if (!await WaitForFileAdded(gtid, remaining))
            {
                return false;
            }
        }
        return true;
    }

    private sealed class InboxItemReceivedPayload
    {
        public TargetDrive TargetDrive { get; set; }
    }
}
