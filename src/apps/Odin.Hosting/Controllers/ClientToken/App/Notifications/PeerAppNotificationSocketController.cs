﻿#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Odin.Hosting.Controllers.Base;
using Odin.Services.AppNotifications.WebSocket;

namespace Odin.Hosting.Controllers.ClientToken.App.Notifications
{
    [ApiController]
    [AuthorizeValidAppToken]
    [Route(AppApiPathConstants.PeerNotificationsV1)]
    public class PeerAppNotificationSocketController(
        PeerAppNotificationHandler notificationHandler,
        IHostApplicationLifetime hostApplicationLifetime)
        : OdinControllerBase
    {
        /// <summary />
        [Route("ws")]
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task Connect()
        {
            if (!HttpContext.WebSockets.IsWebSocketRequest)
            {
                HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            using var cancellationTokenSources = CancellationTokenSource.CreateLinkedTokenSource(
                HttpContext.RequestAborted,
                hostApplicationLifetime.ApplicationStopping);

            using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync(new WebSocketAcceptContext
            {
                DangerousEnableCompression = true
            });

            try
            {
                await notificationHandler.EstablishConnection(webSocket, cancellationTokenSources.Token, WebOdinContext);
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
        }

        [HttpPost("preauth")]
        public IActionResult SocketPreAuth()
        {
            //this only exists so we can use the [AuthorizeValidAppExchangeGrant] attribute to trigger the clienttokenauthhandler
            return Ok();
        }
    }

}