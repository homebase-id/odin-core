﻿using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Youverse.Core.Exceptions;
using Youverse.Core.Logging.CorrelationId;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Base;

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
            catch (YouverseClientException eee)
            {
                await HandleApplicationAccessException(context, eee);
            }
            catch (DriveSecurityException dex)
            {
                await HandleDriveAccessException(context, dex);
            }
            catch (YouverseSecurityException yse)
            {
                await HandleSecurityException(context, yse);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex);
            }
        }

        private Task HandleSecurityException(HttpContext context, YouverseSecurityException exception)
        {
            const int status = 403;
            const string title = "Security Error";

            _logger.LogError(exception, "{ErrorText}", exception.Message);

            var problemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = "No access",
                Extensions =
                {
                    ["correlationId"] = _correlationContext.Id
                }
            };

            var result = JsonSerializer.Serialize(problemDetails, DotYouSystemSerializer.JsonSerializerOptions);
            context.Response.ContentType = "application/problem+json";
            context.Response.StatusCode = status;

            return context.Response.WriteAsync(result);
        }

        //

        private Task HandleApplicationAccessException(HttpContext context, YouverseClientException appException)
        {
            const int status = (int)HttpStatusCode.BadRequest;
            const string title = "Bad Request";

            _logger.LogError(appException, "{ErrorText}", appException.Message);

            string internalErrorMessage = "";
            string stackTrace = "";

            var b = int.TryParse(Environment.GetEnvironmentVariable("DOTYOUCORE_EX_INFO"), out var env);
            if (b && env == 1)
            {
                internalErrorMessage = appException.Message;
                stackTrace = appException.StackTrace ?? "";
            }

            var problemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Extensions =
                {
                    ["errorCode"] = appException.ErrorCode,
                    ["correlationId"] = _correlationContext.Id,
                    ["internalErrorMessage"] = internalErrorMessage,
                    ["stackTrace"] = stackTrace
                }
            };

            var result = JsonSerializer.Serialize(problemDetails);
            context.Response.ContentType = "application/problem+json";
            context.Response.StatusCode = status;

            return context.Response.WriteAsync(result);
        }

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
            //TODO: examine exceptions that inherit from YouverseSecurityException and audit in security log

            const int status = 500;
            const string title = "Internal Server Error";

            string internalErrorMessage = "";
            string stackTrace = "";

            var b = int.TryParse(Environment.GetEnvironmentVariable("DOTYOUCORE_EX_INFO"), out var env);
            if (b && env == 1)
            {
                internalErrorMessage = exception.Message;
                stackTrace = exception.StackTrace ?? "";
            }

            _logger.LogError(exception, "{ErrorText}", exception.Message);

            var problemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Extensions =
                {
                    ["correlationId"] = _correlationContext.Id,
                    ["internalErrorMessage"] = internalErrorMessage,
                    ["stackTrace"] = stackTrace
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