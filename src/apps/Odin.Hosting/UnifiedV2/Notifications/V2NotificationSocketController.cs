#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Hosting.Controllers.Base;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Configuration.VersionUpgrade;
using Odin.Services.Peer.AppNotification;

namespace Odin.Hosting.UnifiedV2.Notifications
{
    /// <summary>
    /// Cookie-less WebSocket entry point for clients (e.g. WASM, native) that cannot send
    /// the app/owner auth cookie on the upgrade. The client authenticates by offering its
    /// bearer token as a value in the Sec-WebSocket-Protocol header, prefixed with
    /// "odin.bearer." — the Kubernetes-style subprotocol bearer pattern. The controller
    /// resolves it to an IOdinContext before accepting the upgrade; once the WS is open
    /// the protocol is the same as the cookie-authenticated route.
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    [Route(UnifiedApiRouteConstants.NotifySocket)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2NotificationSocketController : OdinControllerBase
    {
        // Prefix on a Sec-WebSocket-Protocol value that carries the base64 ClientAuthenticationToken.
        // Mirrors the kube-apiserver "base64url.bearer.authorization.k8s.io.<token>" pattern.
        private const string BearerProtocolPrefix = "odin.bearer.";

        // Application-level subprotocol the server commits to. Echoed back to the client so
        // browsers don't reject the 101 response.
        private const string NegotiatedSubProtocol = "odin.notify.v1";

        private readonly ILogger<V2NotificationSocketController> _logger;
        private readonly AppNotificationHandler _notificationHandler;
        private readonly IHostApplicationLifetime _hostApplicationLifetime;
        private readonly IAppRegistrationService _appRegistrationService;
        private readonly PeerAppNotificationService _peerAppNotificationService;

        public V2NotificationSocketController(
            ILogger<V2NotificationSocketController> logger,
            AppNotificationHandler notificationHandler,
            IHostApplicationLifetime hostApplicationLifetime,
            IAppRegistrationService appRegistrationService,
            PeerAppNotificationService peerAppNotificationService)
        {
            _logger = logger;
            _notificationHandler = notificationHandler;
            _hostApplicationLifetime = hostApplicationLifetime;
            _appRegistrationService = appRegistrationService;
            _peerAppNotificationService = peerAppNotificationService;
        }

        [HttpGet]
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task Connect()
        {
            if (!HttpContext.WebSockets.IsWebSocketRequest)
            {
                HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            var bearerProtocol = HttpContext.WebSockets.WebSocketRequestedProtocols
                .FirstOrDefault(p => p.StartsWith(BearerProtocolPrefix, StringComparison.Ordinal));

            if (bearerProtocol == null)
            {
                HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            // Sec-WebSocket-Protocol values must be RFC 7230 tokens, so the token is sent
            // base64url-encoded (no '=' padding, '-'/'_' instead of '+'/'/'). Restore standard
            // base64 before parsing.
            var token64 = Base64UrlToBase64(bearerProtocol.Substring(BearerProtocolPrefix.Length));

            IOdinContext? authenticatedContext;
            try
            {
                authenticatedContext = await ResolveContextAsync(token64);
            }
            catch (OdinSecurityException e)
            {
                _logger.LogInformation("WS upgrade rejected: {error}", e.Message);
                HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            if (authenticatedContext == null)
            {
                HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            using var cancellationTokenSources = CancellationTokenSource.CreateLinkedTokenSource(
                HttpContext.RequestAborted,
                _hostApplicationLifetime.ApplicationStopping);

            try
            {
                using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync(new WebSocketAcceptContext
                {
                    DangerousEnableCompression = true,
                    SubProtocol = NegotiatedSubProtocol,
                });

                await _notificationHandler.EstablishConnection(webSocket, authenticatedContext, cancellationTokenSources.Token);
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
            catch (InvalidOperationException)
            {
                // this can happen when we need to upgrade
                //System.InvalidOperationException: The response status code for a Extended CONNECT request must be 2XX.
                VersionUpgradeScheduler.SetRequiresUpgradeResponse(HttpContext);
            }
        }

        private async Task<IOdinContext?> ResolveContextAsync(string token64)
        {
            if (!ClientAuthenticationToken.TryParse(token64, out var clientAuthToken))
            {
                throw new OdinSecurityException("Malformed token");
            }

            IOdinContext? ctx;
            string authContextName;

            switch (clientAuthToken.ClientTokenType)
            {
                case ClientTokenType.App:
                    ctx = await _appRegistrationService.GetAppPermissionContextAsync(clientAuthToken, WebOdinContext);
                    authContextName = "websocket-app-token";
                    break;

                default:
                    throw new OdinSecurityException("Invalid Client Token Type");
            }

            ctx?.SetAuthContext(authContextName);
            return ctx;
        }

        private static string Base64UrlToBase64(string base64Url)
        {
            var s = base64Url.Replace('-', '+').Replace('_', '/');
            var padLen = (4 - s.Length % 4) % 4;
            return padLen == 0 ? s : s + new string('=', padLen);
        }
    }
}
