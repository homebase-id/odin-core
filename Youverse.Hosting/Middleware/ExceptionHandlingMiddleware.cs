using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Youverse.Core.Exceptions;
using Youverse.Core.Logging.CorrelationId;

namespace Youverse.Hosting.Middleware
{
    public class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionHandlingMiddleware> _logger;
        private readonly ICorrelationContext _correlationContext;

        //

        public ExceptionHandlingMiddleware(
            RequestDelegate next,
            ILogger<ExceptionHandlingMiddleware> logger,
            ICorrelationContext correlationContext)
        {
            _next = next;
            _logger = logger;
            _correlationContext = correlationContext;
        }

        //

        public async Task Invoke(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (DriveSecurityException dex)
            {
                await HandleDriveAccessException(context, dex);
            }
            catch (SharedSecretException sharedSecretException)
            {
                await HandleSharedSecretException(context, sharedSecretException);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex);
            }
        }

        private Task HandleSharedSecretException(HttpContext context, SharedSecretException sharedSecretException)
        {
            const int status = 403;
            const string title = "Shared Secret Required";

            _logger.LogError(sharedSecretException, "{ErrorText}", sharedSecretException.Message);

            var problemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = "The request body you provided must be formatted as a SharedSecretEncryptedPayload.",
                Extensions =
                {
                    ["correlationId"] = _correlationContext.Id
                }
            };

            var result = JsonSerializer.Serialize(problemDetails);
            context.Response.ContentType = "application/problem+json";
            context.Response.StatusCode = status;

            return context.Response.WriteAsync(result);
        }

        //

        private Task HandleDriveAccessException(HttpContext context, Exception exception)
        {
            const int status = 403;
            const string title = "Access Denied";

            _logger.LogError(exception, "{ErrorText}", exception.Message);

            var problemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Extensions =
                {
                    ["correlationId"] = _correlationContext.Id
                }
            };

            var result = JsonSerializer.Serialize(problemDetails);
            context.Response.ContentType = "application/problem+json";
            context.Response.StatusCode = status;

            return context.Response.WriteAsync(result);
        }


        private Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            //TODO: examine exceptoins that inheit from YouverseSecurityException and audit in security log

            const int status = 500;
            const string title = "Internal Server Error";

            _logger.LogError(exception, "{ErrorText}", exception.Message);

            var problemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Extensions =
                {
                    ["correlationId"] = _correlationContext.Id
                }
            };

            var result = JsonSerializer.Serialize(problemDetails);
            context.Response.ContentType = "application/problem+json";
            context.Response.StatusCode = status;

            return context.Response.WriteAsync(result);
        }

        //
    }
}