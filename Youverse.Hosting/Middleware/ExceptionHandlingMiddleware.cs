using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Youverse.Core.Exceptions.Client;
using Youverse.Core.Exceptions.Server;
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
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex);
            }
        }

        //


        //
        // RULES:
        // - Client triggered exceptions (NotFound, BadReques, etc), includes status and text in response.
        // - Server triggered exceptions (explicitly throwing ServerException), includes status and text in repsonse.
        // - All other exceptions will include status 500 and text "internal server error" in response.
        //
        // All exceptions, except client-exceptions are logged.
        //
        private Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            var status = 500;
            var title = "Internal Server Error";
            var logException = true;

            switch (exception)
            {
                case ServerException se:
                    status = (int)se.HttpStatusCode;
                    title = se.Message;
                    break;
                case ClientException ce:
                    logException = false;
                    status = (int)ce.HttpStatusCode;
                    title = ce.Message;
                    break;
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

            var result = JsonSerializer.Serialize(problemDetails);
            context.Response.ContentType = "application/problem+json";
            context.Response.StatusCode = status;

            return context.Response.WriteAsync(result);
        }

        //

    }
}
