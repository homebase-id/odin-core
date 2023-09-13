using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Logging.CorrelationId;
using Odin.Hosting.ApiExceptions;
using Odin.Hosting.ApiExceptions.Client;
using Odin.Hosting.ApiExceptions.Server;

namespace Odin.Hosting.Middleware
{
    public class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionHandlingMiddleware> _logger;
        private readonly ICorrelationContext _correlationContext;
        private readonly bool _sendInternalErrorDetailsToClient;

        //

        public ExceptionHandlingMiddleware(
            RequestDelegate next,
            ILogger<ExceptionHandlingMiddleware> logger,
            ICorrelationContext correlationContext,
            IHostEnvironment env)
        {
            _next = next;
            _logger = logger;
            _correlationContext = correlationContext;
            _sendInternalErrorDetailsToClient = env.IsDevelopment();
        }

        //

        public async Task Invoke(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (OdinClientException e) // => HTTP 400
            {
                // SEB:TODO
                // OdinClientException is used in a lot of places.
                // We need to go through them all and determine if any should map to something
                // different than 400, in which case the code should throw a different exception.
                await HandleExceptionAsync(context, new BadRequestException(e.Message, e.ErrorCode, e));
            }
            catch (OdinRemoteIdentityException e) // => HTTP 503
            {
                var message = $"Remote identity host failed: {e.Message}";
                await HandleExceptionAsync(context, new ServiceUnavailableException(message, e));
            }
            catch (DriveSecurityException e) // => HTTP 403
            {
                // SEB:TODO
                // Does it make sense to return 403 to client? Is it really an internal server error
                // because of bad state?
                var message = $"{ForbiddenException.DefaultErrorMessage}: {e.Message}";
                await HandleExceptionAsync(context, new ForbiddenException(message, inner: e));
            }
            catch (OdinSecurityException e) // => HTTP 403
            {
                // SEB:TODO
                // OdinSecurityException is used in a lot of places.
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
            var problemDetails = new ProblemDetails
            {
                Status = 500,
                Title = "Internal Server Error",
                Type = "https://tools.ietf.org/html/rfc7231",
                Extensions =
                {
                    ["correlationId"] = _correlationContext.Id
                }
            };

            if (exception is ApiException ae)
            {
                problemDetails.Status = (int)ae.HttpStatusCode;
            }

            if (exception is ClientException ce)
            {
                problemDetails.Title = ce.Message;
                problemDetails.Extensions["errorCode"] = ce.OdinClientErrorCode;
            }
            else
            {
                _logger.LogError(exception, "{ErrorText}", exception.Message);
            }

            if (_sendInternalErrorDetailsToClient)
            {
                problemDetails.Title = exception.Message;
                problemDetails.Extensions["stackTrace"] = exception.StackTrace;
            }

            var result = JsonSerializer.Serialize(problemDetails);

            if (!context.Response.HasStarted)
            {
                // Avoids error "Headers are read-only, response has already started."
                context.Response.ContentType = "application/problem+json";
                context.Response.StatusCode = problemDetails.Status.Value;
            }

            return context.Response.WriteAsync(result);
        }
    }
}