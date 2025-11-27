using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Hosting.Controllers.Anonymous.Cdn;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Peer;
using Odin.Hosting.Controllers.ClientToken.App;
using Odin.Hosting.Controllers.ClientToken.Guest;
using Odin.Hosting.Controllers.Home.Auth;
using Odin.Services.LinkPreview;

namespace Odin.Hosting.Middleware
{
    /// <summary>
    /// Decrypts requests and encrypts responses using the shared secret.  This does not handle websockets, they are
    /// handled by their own controller
    /// </summary>
    public class SharedSecretEncryptionMiddleware
    {
        private const string SharedSecretQueryStringParam = "ss";

        private readonly RequestDelegate _next;
        private readonly ILogger<SharedSecretEncryptionMiddleware> _logger;
        private readonly List<string> _ignoredPathsForRequests;

        /// <summary>
        /// Paths that should not have their responses encrypted
        /// </summary>
        private readonly List<string> _ignoredPathsForResponses;
        //

        /// <summary />
        public SharedSecretEncryptionMiddleware(
            RequestDelegate next,
            ILogger<SharedSecretEncryptionMiddleware> logger)
        {
            _next = next;
            _logger = logger;


            _ignoredPathsForRequests =
            [
                PeerApiPathConstants.BasePathV1, //TODO: temporarily allowing all perimeter traffic not use shared secret
                $"{HomeApiPathConstants.AuthV1}/is-authenticated",

                OwnerApiPathConstants.YouAuthV1,
                OwnerApiPathConstants.AuthV1,
                $"{OwnerApiPathConstants.DriveV1}/files/upload",
                $"{OwnerApiPathConstants.DriveV1}/files/uploadpayload",
                $"{OwnerApiPathConstants.PeerSenderV1}/files/send",
                $"{OwnerApiPathConstants.DriveV1}/files/update",
                $"{OwnerApiPathConstants.SecurityRecoveryV1}/verify-recovery-key",
                $"{OwnerApiPathConstants.SecurityRecoveryV1}/verify-password",
                $"{OwnerApiPathConstants.SecurityRecoveryV1}/update-recovery-email",
                $"{OwnerApiPathConstants.SecurityRecoveryV1}/verify-email",
                $"{OwnerApiPathConstants.SecurityRecoveryV1}/recovery-info", // <wtf!?
                $"{OwnerApiPathConstants.SecurityRecoveryV1}/needs-attention", // <wtf^2!?
                $"{OwnerApiPathConstants.SecurityRecoveryV1}/update-monthly-security-health-report-status", // <wtf^2!?
                $"{OwnerApiPathConstants.SecurityRecoveryV1}/monthly-security-health-report-status", // <wtf^2!?

                $"{GuestApiPathConstants.DriveV1}/files/upload",
                $"{GuestApiPathConstants.DriveV1}/files/uploadpayload",
                $"{GuestApiPathConstants.PeerSenderV1}/files/send",
                $"{GuestApiPathConstants.DriveV1}/files/update",

                $"{AppApiPathConstants.PeerV1}/app/process", //TODO: why is this here??
                $"{AppApiPathConstants.PeerSenderV1}/files/send",

                $"{AppApiPathConstants.DriveV1}/files/upload",
                $"{AppApiPathConstants.DriveV1}/files/update",
                $"{AppApiPathConstants.DriveV1}/files/uploadpayload",
                $"{AppApiPathConstants.AuthV1}/logout",
                $"{AppApiPathConstants.NotificationsV1}/preauth",
                $"{AppApiPathConstants.PeerNotificationsV1}/preauth",
                $"{GuestApiPathConstants.PeerNotificationsV1}/preauth"
            ];


            //Paths that should not have their responses encrypted with shared secret
            _ignoredPathsForResponses =
            [
                $"{OwnerApiPathConstants.DriveV1}/files/payload",
                $"{OwnerApiPathConstants.DriveV1}/files/thumb",

                $"{OwnerApiPathConstants.DriveQuerySpecializedClientUniqueId}/payload",
                $"{OwnerApiPathConstants.DriveQuerySpecializedClientUniqueId}/thumb",

                $"{OwnerApiPathConstants.PeerV1}/query/payload",
                $"{OwnerApiPathConstants.PeerV1}/query/thumb",

                $"{OwnerApiPathConstants.PeerV1}/query/payload_byglobaltransitid",
                $"{OwnerApiPathConstants.PeerV1}/query/thumb_byglobaltransitid",

                $"{AppApiPathConstants.DriveV1}/files/payload",
                $"{AppApiPathConstants.DriveV1}/files/thumb",

                $"{AppApiPathConstants.PeerQueryV1}/payload",
                $"{AppApiPathConstants.PeerQueryV1}/thumb",

                $"{AppApiPathConstants.DriveQuerySpecializedClientUniqueId}/payload",
                $"{AppApiPathConstants.DriveQuerySpecializedClientUniqueId}/thumb",

                $"{AppApiPathConstants.PeerQueryV1}/payload_byglobaltransitid",
                $"{AppApiPathConstants.PeerQueryV1}/thumb_byglobaltransitid",

                $"{GuestApiPathConstants.DriveQuerySpecializedClientUniqueId}/payload",
                $"{GuestApiPathConstants.DriveQuerySpecializedClientUniqueId}/thumb",

                $"{GuestApiPathConstants.DriveV1}/files/thumb",
                $"{GuestApiPathConstants.DriveV1}/files/payload",

                $"{CdnApiPathConstants.DriveV1}/files/thumb",
                $"{CdnApiPathConstants.DriveV1}/files/payload",
            ];

            _ignoredPathsForResponses.AddRange(_ignoredPathsForRequests);

            // for link-preview add all supported extension for thumbnails
            var allExtensions = MimeTypeHelper.SubtypeToExtension.Select(kvp => kvp.Value);
            foreach (var extension in allExtensions)
            {
                _ignoredPathsForResponses.Add($"{GuestApiPathConstants.DriveV1}/files/thumb{extension}");
                _ignoredPathsForResponses.Add($"{GuestApiPathConstants.DriveQuerySpecializedClientUniqueId}/thumb{extension}");
            }
        }

        //

        public async Task Invoke(HttpContext context)
        {
            if (ShouldDecryptRequest(context))
            {
                await DecryptRequest(context);
            }

            if (ShouldEncryptResponse(context))
            {
                using (var responseStream = new MemoryStream())
                {
                    //create a separate response stream to collect all the content being written
                    var originalBody = context.Response.Body;
                    context.Response.Body = responseStream;

                    try
                    {
                        await _next(context);

                        responseStream.Seek(0L, SeekOrigin.Begin);
                        await EncryptResponse(context, originalBody);
                    }
                    catch (Exception)
                    {
                        context.Response.Body = originalBody;
                        throw;
                    }
                }
            }
            else
            {
                await _next(context);
            }
        }

        private async Task DecryptRequest(HttpContext context)
        {
            var request = context.Request;

            try
            {
                if (request.Method.ToUpper() == "GET")
                {
                    if (request.Query.TryGetValue(SharedSecretQueryStringParam, out var qs) == false ||
                        string.IsNullOrEmpty(qs.FirstOrDefault()) ||
                        string.IsNullOrWhiteSpace(qs.FirstOrDefault()))
                    {
                        throw new OdinClientException("Querystring must be encrypted", OdinClientErrorCode.SharedSecretEncryptionIsInvalid);
                    }

                    var newQsBytes = SharedSecretEncryptedPayload.Decrypt(qs.FirstOrDefault() ?? "", this.GetSharedSecret(context));
                    var newQs = newQsBytes.ToStringFromUtf8Bytes();
                    var prefix = newQs.FirstOrDefault() == '?' ? "" : "?";
                    request.QueryString = new QueryString($"{prefix}{newQs}");
                    if (_logger.IsEnabled(LogLevel.Trace))
                    {
                        _logger.LogTrace("qs: {querystring}", request.QueryString.ToString());
                    }
                }
                else if (request.Method.ToUpper() == "DELETE")
                {
                    // Some hand-holding for delete verbs; i don't understand why i have to do this, however.
                    var bytes = request.Body.ToByteArray();
                    if (bytes.Length > 0)
                    {
                        var decryptedBytes = await SharedSecretEncryptedPayload.Decrypt(new MemoryStream(bytes),
                            this.GetSharedSecret(context), context.RequestAborted);
                        //update the body with the decrypted json file so it can be read down stream as expected
                        request.Body = new MemoryStream(decryptedBytes);
                    }
                }
                else
                {
                    var decryptedBytes =
                        await SharedSecretEncryptedPayload.Decrypt(request.Body, this.GetSharedSecret(context), context.RequestAborted);

                    //update the body with the decrypted json file so it can be read down stream as expected
                    request.Body = new MemoryStream(decryptedBytes);
                }
            }
            catch (JsonException)
            {
                throw new OdinClientException(
                    "Failed to decrypt shared secret payload.  Ensure you've provided a body of json formatted as SharedSecretEncryptedPayload",
                    OdinClientErrorCode.SharedSecretEncryptionIsInvalid);
            }
            catch (CryptographicException ex) when (ex.Message.Contains("Padding is invalid and cannot be removed"))
            {
                // We can get here if the encryption keys don't match. Go figure.
                throw new OdinClientException(
                    "Failed to decrypt shared secret payload. Ensure encryption keys are matching.",
                    OdinClientErrorCode.SharedSecretEncryptionIsInvalid);
            }
        }

        private async Task EncryptResponse(HttpContext context, Stream originalBody)
        {
            if (context.Response.HasStarted)
            {
                // Avoids error "Headers are read-only, response has already started."
                // We can't change or undo an already started response.
                return;
            }

            //if a controller tells us no content, write nothing to the stream
            if (context.Response.StatusCode == (int)HttpStatusCode.NoContent)
            {
                context.Response.Body = originalBody;
                return;
            }

            var key = this.GetSharedSecret(context);
            var responseBytes = context.Response.Body.ToByteArray();
            var finalBytes = JsonSerializer.SerializeToUtf8Bytes(
                SharedSecretEncryptedPayload.Encrypt(responseBytes, key),
                typeof(SharedSecretEncryptedPayload),
                OdinSystemSerializer.JsonSerializerOptions);

            // context.Response.Headers.Append("X-SSE", "1");
            context.Response.Headers.ContentType = "application/json";
            context.Response.ContentLength = finalBytes.Length;
            await new MemoryStream(finalBytes).CopyToAsync(originalBody);

            context.Response.Body = originalBody;
        }

        private SensitiveByteArray GetSharedSecret(HttpContext context)
        {
            var dotYouContext = context.RequestServices.GetRequiredService<IOdinContext>();
            var key = dotYouContext.PermissionsContext.SharedSecretKey;
            return key;
        }

        private bool ShouldDecryptRequest(HttpContext context)
        {
            if (context.Request.Method.ToUpper() == "GET" && !context.Request.Query.Any())
            {
                return false;
            }

            if (context.WebSockets.IsWebSocketRequest)
            {
                return false;
            }

            if (!context.Request.Path.StartsWithSegments("/api") || !CallerMustHaveSharedSecret(context))
            {
                return false;
            }

            if (context.Request.Method.ToUpper() == "POST" && context.Request.Headers.ContentLength == 0)
            {
                return false;
            }

            if (context.Request.Method.ToUpper() == "GET" && context.Request.QueryString.HasValue == false)
            {
                return false;
            }

            if (context.Request.Method.ToUpper() == "OPTIONS")
            {
                return false;
            }

            return !_ignoredPathsForRequests.Any(p => context.Request.Path.StartsWithSegments(p));
        }

        private bool ShouldEncryptResponse(HttpContext context)
        {
            if (context.WebSockets.IsWebSocketRequest)
            {
                return false;
            }

            if (!context.Request.Path.StartsWithSegments("/api") || !CallerMustHaveSharedSecret(context))
            {
                return false;
            }

            return !_ignoredPathsForResponses.Any(p => context.Request.Path.StartsWithSegments(p));
        }

        private bool CallerMustHaveSharedSecret(HttpContext context)
        {
            var dotYouContext = context.RequestServices.GetRequiredService<IOdinContext>();
            return !dotYouContext.Caller.IsAnonymous && dotYouContext.Caller.SecurityLevel != SecurityGroupType.System;
        }
    }
}