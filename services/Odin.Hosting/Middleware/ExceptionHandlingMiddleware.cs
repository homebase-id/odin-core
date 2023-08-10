using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Exceptions.Client;
using Odin.Core.Exceptions.Server;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Serialization;

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
            _sendInternalErrorDetailsToClient =
                env.IsDevelopment() || Environment.GetEnvironmentVariable("DOTYOUCORE_EX_INFO") == "1";
        }

        //

        /// <summary/>
        public async Task Invoke(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (OdinClientException eee)
            {
                await HandleApplicationAccessException(context, eee);
            }
            catch (OdinRemoteIdentityException rie)
            {
                await HandleRemoteServerException(context, rie);
            }
            catch (DriveSecurityException dex)
            {
                await HandleDriveAccessException(context, dex);
            }
            catch (UnauthorizedAccessException uex)
            {
                await HandleDriveAccessException(context, uex);
            }
            catch (OdinSecurityException yse)
            {
                await HandleSecurityException(context, yse);
            }
            catch (IOException iox)
            {
                await HandleIoException(context, iox);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex);
            }
        }

        private Task HandleSecurityException(HttpContext context, OdinSecurityException exception)
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

            var result = JsonSerializer.Serialize(problemDetails, OdinSystemSerializer.JsonSerializerOptions);
            context.Response.ContentType = "application/problem+json";
            context.Response.StatusCode = status;

            return context.Response.WriteAsync(result);
        }

        private Task HandleIoException(HttpContext context, IOException exception)
        {
            const int status = 404;
            const string title = "Not Found";

            _logger.LogError(exception, "{ErrorText}", $"IOException - {exception.GetType().Name} - {exception.Message}");

            var problemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = "File or directory not found",
                Extensions =
                {
                    ["correlationId"] = _correlationContext.Id
                }
            };

            var result = JsonSerializer.Serialize(problemDetails, OdinSystemSerializer.JsonSerializerOptions);
            context.Response.ContentType = "application/problem+json";
            context.Response.StatusCode = status;

            return context.Response.WriteAsync(result);
        }

        //

        private Task HandleRemoteServerException(HttpContext context, OdinRemoteIdentityException appException)
        {
            const int status = (int)HttpStatusCode.ServiceUnavailable;
            const string title = "Remote Identity Server failed";

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
        private Task HandleApplicationAccessException(HttpContext context, OdinClientException appException)
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

            if (exception is OdinApiException ae)
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
            context.Response.ContentType = "application/problem+json";
            context.Response.StatusCode = problemDetails.Status.Value;

            return context.Response.WriteAsync(result);
        }
    }
}