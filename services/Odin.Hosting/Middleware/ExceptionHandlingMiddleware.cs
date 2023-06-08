using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Youverse.Core.Exceptions;
using Youverse.Core.Exceptions.Client;
using Youverse.Core.Exceptions.Server;
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
            catch (YouverseClientException eee)
            {
                await HandleApplicationAccessException(context, eee);
            }
            catch (YouverseRemoteIdentityException rie)
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
            catch (YouverseSecurityException yse)
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

        private Task HandleRemoteServerException(HttpContext context, YouverseRemoteIdentityException appException)
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

        //
        
        private Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            var status = 500;
            var title = "Internal Server Error";
            var stackTrace = _sendInternalErrorDetailsToClient ? exception.StackTrace : null;
            var logException = true;

            if (exception is ClientException ce)
            {
                logException = false;
                status = (int)ce.HttpStatusCode;
                title = ce.Message;
            }
            else if (exception is ServerException se)
            {
                status = (int)se.HttpStatusCode;
                title = se.Message;
            }
            else if (_sendInternalErrorDetailsToClient)
            {
                title = exception.Message;
            }

            if (logException)
            {
                _logger.LogError(exception, "{ErrorText}", exception.Message);
            }

            var problemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Type = "https://tools.ietf.org/html/rfc7231",
                Extensions =
                {
                    ["correlationId"] = _correlationContext.Id
                }
            };
            if (_sendInternalErrorDetailsToClient)
            {
                problemDetails.Extensions["stackTrace"] = stackTrace; 
            }

            var result = JsonSerializer.Serialize(problemDetails);
            context.Response.ContentType = "application/problem+json";
            context.Response.StatusCode = status;

            return context.Response.WriteAsync(result);
        }
        
    }
}