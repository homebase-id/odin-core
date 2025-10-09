using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Odin.Services.AppNotifications.WebSocket.SignalR;

#nullable enable

/// <summary>
/// Strongly-typed SignalR hub for CLIENT → SERVER communication
/// Receives and handles messages invoked by clients
/// Inherits from Hub<INotificationClient> to get compile-time checking of client method calls
/// Has access to Context (ConnectionId, User) for identifying the caller
/// Can also send messages back to clients using Clients.All, Clients.Caller, etc.
/// </summary>
public class NotificationHub(ILogger<NotificationHub> logger) : Hub<INotificationClient>
{
    private const string MethodReceiveNotification = "ReceiveNotification";

    //

    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
        logger.LogDebug("Client connected: {ConnectionId}", Context.ConnectionId);
    }

    //

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
        logger.LogDebug("Client disconnected: {ConnectionId}", Context.ConnectionId);
    }

    //



}