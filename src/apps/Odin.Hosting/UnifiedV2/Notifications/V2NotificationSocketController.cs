#nullable enable

using System;
using System.Linq;
using System.Runtime.CompilerServices;
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

[assembly: InternalsVisibleTo("Odin.Hosting.Tests")]
[assembly: InternalsVisibleTo("Odin.Hosting.Tests.V2")]

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
        [Route(UnifiedApiRouteConstants.NotifySocket)]
        [ApiExplorerSettings(IgnoreApi = true)]
        public Task Connect()
        {
            return HandleAsync();
        }

        // Browser/WASM clients upgrade over HTTP/2 Extended CONNECT (RFC 8441:
        // :method=CONNECT, :protocol=websocket). MVC routing rejects those with
        // 405 Allow: GET when only [HttpGet] is present, so the wasm-facing route
        // accepts both verbs. Native HTTP/1.1 clients keep using the [HttpGet]
        // route above — switching that route to AcceptVerbs caused 401s on
        // Ktor CIO/OkHttp/Darwin (the bearer subprotocol value got dropped
        // before the controller ran).
        [AcceptVerbs("GET", "CONNECT")]
        [Route(UnifiedApiRouteConstants.NotifySocketWasm)]
        [ApiExplorerSettings(IgnoreApi = true)]
        public Task ConnectWasm()
        {
            return HandleAsync();
        }

        private async Task HandleAsync()
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
                // Diagnostics for the intermittent native-client 401: log the wire
                // protocol (HTTP/1.1 vs HTTP/2 Extended CONNECT — reveals a proxy
                // forwarding the upgrade over h2) and the requested subprotocols
                // MINUS the bearer (so we never log the credential). If this shows
                // protocols=[odin.notify.v1] with the bearer absent, the bearer was
                // stripped/reordered before reaching the controller (proxy/engine),
                // not a client encoding bug.
                _logger.LogWarning(
                    "V2 WS upgrade rejected (401): no '{prefix}*' subprotocol present. " +
                    "httpProtocol={protocol} isWebSocketRequest={isWs} nonBearerProtocols=[{protocols}]",
                    BearerProtocolPrefix,
                    HttpContext.Request.Protocol,
                    HttpContext.WebSockets.IsWebSocketRequest,
                    string.Join(", ", HttpContext.WebSockets.WebSocketRequestedProtocols
                        .Where(p => !p.StartsWith(BearerProtocolPrefix, StringComparison.Ordinal))));
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
                _logger.LogWarning("V2 WS upgrade rejected (401): token parsed but security check failed: {error} " +
                                   "httpProtocol={protocol}", e.Message, HttpContext.Request.Protocol);
                HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            if (authenticatedContext == null)
            {
                _logger.LogWarning("V2 WS upgrade rejected (401): app permission context resolved to null for a " +
                                   "parsed token. httpProtocol={protocol}", HttpContext.Request.Protocol);
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

            // GetAppPermissionContextAsync returns a token-keyed CACHED IOdinContext that is
            // shared across requests. Calling SetAuthContext on it mutates that shared instance:
            // the first WS upgrade for a token sets AuthContext, and every subsequent upgrade for
            // the same token then throws OdinSecurityException("Cannot reset auth context") -> 401
            // until the cache entry expires. That is the intermittent reconnect-401 flap. Clone
            // first so the per-connection auth context never touches the cached entry.
            var resolved = ctx?.Clone();
            resolved?.SetAuthContext(authContextName);
            return resolved;
        }

        internal static string Base64UrlToBase64(string base64Url)
        {
            var s = base64Url.Replace('-', '+').Replace('_', '/');
            var padLen = (4 - s.Length % 4) % 4;
            return padLen == 0 ? s : s + new string('=', padLen);
        }
    }
}
