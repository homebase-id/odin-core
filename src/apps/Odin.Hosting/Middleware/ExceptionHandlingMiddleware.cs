using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Serialization;
using Odin.Hosting.ApiExceptions;
using Odin.Hosting.ApiExceptions.Client;
using Odin.Hosting.ApiExceptions.Server;
using Odin.Services.Configuration.VersionUpgrade;

namespace Odin.Hosting.Middleware
{
    public class ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        ICorrelationContext correlationContext,
        IHostEnvironment env)
    {
        private readonly bool _sendInternalErrorDetailsToClient = env.IsDevelopment();

        //

        public async Task Invoke(HttpContext context)
        {
            try
            {
                await next(context);
            }
            catch (OdinClientException e) // => HTTP 400
            {
                // SEB:TODO OdinClientException is used in a lot of places.
                // We need to go through them all and determine if any should map to something
                // different than 400, in which case the code should throw a different exception.
                await HandleExceptionAsync(context, new BadRequestException(e.Message, e.ErrorCode, e));
            }
            catch (OdinRemoteIdentityException e) // => HTTP 503
            {
                var message = $"Remote identity host failed: {e.Message}";
                await HandleExceptionAsync(context, new ServiceUnavailableException(message, e));
            }
            catch (VersionUpgradeRunningException e) // => HTTP 503
            {
                var message = $"Version Upgrade is in progress";
                await HandleExceptionAsync(context, new ServiceUnavailableException(message, e));
            }
            catch (OdinSecurityException e) // => HTTP 403
            {
                // SEB:TODO OdinSecurityException is used in a lot of places.
                // We need to go through them all and determine if any should map to something
                // different than 403, in which case the code should throw a different exception.
                var message = $"{ForbiddenException.DefaultErrorMessage}: {e.Message}";
                await HandleExceptionAsync(context, new ForbiddenException(message, inner: e));
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex);
            }
        }

        //

        private Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            // We're not allowed to write anything back on a websocket CONNECT,
            // so for now just log whatever it is as an error.
            if (context.WebSockets.IsWebSocketRequest && context.Request.Method == "CONNECT")
            {
                logger.LogError(exception, "{ErrorText}", exception.Message);
                return Task.CompletedTask;
            }

            var problemDetails = new ProblemDetails
            {
                Status = 500,
                Title = "Internal Server Error",
                Type = "https://tools.ietf.org/html/rfc7231",
                Extensions =
                {
                    ["correlationId"] = correlationContext.Id
                }
            };

            if (IsCancellationException(exception))
            {
                problemDetails.Status = 499;
                problemDetails.Title = "Operation was cancelled";
            }
            else if (exception is ApiException ae)
            {
                problemDetails.Status = (int)ae.HttpStatusCode;
                if (exception is ClientException ce)
                {
                    problemDetails.Title = ce.Message;
                    problemDetails.Extensions["errorCode"] = ce.OdinClientErrorCode;
                }
            }

            switch (problemDetails.Status)
            {
                case 499:
                    logger.LogWarning("{WarningText}", exception.Message);
                    break;
                case >= 500:
                    logger.LogError(exception, "{ErrorText}", exception.Message);
                    break;
            }

            if (_sendInternalErrorDetailsToClient)
            {
                problemDetails.Title = exception.Message;
                problemDetails.Extensions["stackTrace"] = exception.StackTrace;
            }

            var result = OdinSystemSerializer.Serialize(problemDetails);

            if (!context.Response.HasStarted)
            {
                // Avoids error "Headers are read-only, response has already started."
                context.Response.ContentType = "application/problem+json";
                context.Response.StatusCode = problemDetails.Status.Value;
            }


            return context.Response.WriteAsync(result);
        }

        //

        // SEB:NOTE
        // This is a last resort exception filter.
        // Exceptions should be caught and handled as close to the source
        // as possble. Only rely on the below if there are no other way.
        private static bool IsCancellationException(Exception ex)
        {
            switch (ex)
            {
                case ConnectionResetException:
                case IOException when ex.Message == "The client reset the request stream.":
                case IOException when ex.Message == "The request stream was aborted.":
                case OperationCanceledException:
                case WebSocketException when ex.Message == "The remote party closed the WebSocket connection without completing the close handshake.":
                    return true;
                default:
                    return ex is AggregateException aex && aex.InnerExceptions.All(IsCancellationException);
            }
        }
    }
}