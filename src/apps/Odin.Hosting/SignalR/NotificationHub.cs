using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Hosting.Controllers.ClientToken.App;
using Odin.Hosting.Controllers.OwnerToken;
using Odin.Hosting.SignalR.Models;
using Odin.Services.Authentication.Owner;
using Odin.Services.Base;

namespace Odin.Hosting.SignalR;

#nullable enable

//
// AppNotificationHandler (common for owner and app):
//
// server → client:
//  Through MediatR notifications:
//   - IClientNotification
//   - IDriveNotification
//   - InboxItemReceivedNotification
//
// client → server:
//  Through websocket recv loop:
//    - SocketCommandType.EstablishConnectionRequest:
//      - does something with list of drives sent from client
//      - sends back EstablishConnectionResponse
//
//    - SocketCommandType.ProcessTransitInstructions:
//      - makes backend call ProcessInboxAsync(TargetDrive targetDrive, IOdinContext odinContext)
//
//    - SocketCommandType.ProcessInbox
//      - makes backend call ProcessInboxAsync(TargetDrive targetDrive, IOdinContext odinContext)
//
//    - SocketCommandType.Ping
//      - sends back Pong (keep alive?)
//
//
//
// PeerAppNotificationHandler (for guest / anonymous):
//
// - NOTE usage of odd SocketAuthenticationPackage
//
// server → client:
//  Through MediatR notifications:
//   - IClientNotification
//   - IDriveNotification notification
//
// client → server:
//  Through websocket recv loop:
//    - SocketCommandType.EstablishConnectionRequest:
//      - does something with list of drives sent from client
//      - sends back EstablishConnectionResponse
//
//    - SocketCommandType.Ping
//      - sends back Pong (keep alive?)
//
//

//
// Abstract
//

/// <summary>
/// Strongly-typed SignalR hub for CLIENT → SERVER communication
/// Receives and handles messages invoked by clients
/// Inherits from Hub[INotificationClient] to get compile-time checking of client method calls
/// Has access to Context (ConnectionId, User) for identifying the caller
/// Can also send messages back to clients using Clients.All, Clients.Caller, etc.
/// </summary>
public abstract class AbstractNotificationHub(ILogger logger) : Hub<INotificationClient>
{
    private IOdinContext? _odinContext;
    protected IOdinContext? OdinContext
    {
        get => _odinContext ??= Context.Items["OdinContext"] as IOdinContext;
        set
        {
            _odinContext = value;
            Context.Items["OdinContext"] = value;
        }
    }

    //

    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();

        var httpContext = Context.GetHttpContext();
        if (httpContext != null)
        {
            logger.LogDebug("OnConnectedAsync:{ConnectionId} {method} {schema} {path}",
                Context.ConnectionId,
                httpContext.Request.Method,
                httpContext.Request.Scheme,
                httpContext.Request.Path);

            var odinContext = httpContext.RequestServices.GetRequiredService<IOdinContext>();
            OdinContext = odinContext.Clone();
        }
    }

    //

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
        logger.LogDebug("Signalr client disconnected: {ConnectionId}", Context.ConnectionId);
    }

    //

    public virtual Task SendTextMessage(TextMessage message)
    {
        logger.LogDebug("XXXXXXXXXXXXXXXX  Message received from client: {message}", message.Message);
        return Task.CompletedTask;
    }

    //

    public virtual Task TestConnection()
    {
        var user = Context.User;
        var isAuthenticated = user?.Identity?.IsAuthenticated ?? false;
        var authType = user?.Identity?.AuthenticationType;
        var userName = user?.Identity?.Name;
        var claims = user?.Claims?.Select(c => $"{c.Type}={c.Value}").ToList() ?? new List<string>();

        logger.LogInformation(
            "TestConnection - IsAuth: {IsAuth}, AuthType: {AuthType}, Name: {Name}, Claims: {Claims}",
            isAuthenticated,
            authType,
            userName,
            string.Join(", ", claims)
        );

        logger.LogInformation("OdinContext caller {caller}", OdinContext?.Caller.OdinId?.DomainName);

        return Task.CompletedTask;
    }

    //

    protected void SanityCheckAuthentication(string expectedAuthenticationType)
    {
        // Sanity #1
        var authenticationType = Context.GetHttpContext()?.User.Identity?.AuthenticationType ?? "";
        if (authenticationType != expectedAuthenticationType)
        {
            throw new OdinSystemException($"AuthenticationType: '{authenticationType}' != '{expectedAuthenticationType}'");
        };

        // Sanity #2
        if (OdinContext?.Caller == null)
        {
            throw new OdinSystemException("OdinContext?.Caller is not assigned");
        }
    }
}

//
// Concrete
//

[AuthorizeValidOwnerToken]
public class OwnerNotificationHub(ILogger<OwnerNotificationHub> logger) : AbstractNotificationHub(logger)
{
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
        SanityCheckAuthentication(OwnerAuthConstants.SchemeName);
    }
}

[AuthorizeValidAppToken]
public class AppNotificationHub(ILogger<AppNotificationHub> logger) : AbstractNotificationHub(logger)
{
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
        SanityCheckAuthentication(YouAuthConstants.AppSchemeName);
    }
}

[AllowAnonymous]
public class GuestNotificationHub(ILogger<GuestNotificationHub> logger) : AbstractNotificationHub(logger);