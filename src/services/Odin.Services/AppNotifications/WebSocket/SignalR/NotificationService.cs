namespace Odin.Services.AppNotifications.WebSocket.SignalR;

/// <summary>
/// Service for SERVER → CLIENT communication via IHubContext
/// Wraps IHubContext with readable, action-oriented method names for pushing messages to clients
/// Use this service from background services, controllers, or any external service that needs to send messages to clients
/// Makes server-side code more intuitive while maintaining the client interface contract
/// </summary>
public class NotificationService
{

}