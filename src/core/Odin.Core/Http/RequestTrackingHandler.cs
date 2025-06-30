using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Odin.Core.Http;

#nullable enable

internal sealed class RequestTrackingHandler(ILogger logger, HttpMessageHandler innerHandler, HandlerEntry entry)
    : DelegatingHandler(innerHandler)
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        entry.IncrementActiveRequests();
        try
        {
            if (logger.IsEnabled(LogLevel.Trace))
            {
                logger.LogTrace("BeforeSend ActiveRequests={count} for handler {key}: ", entry.ActiveRequests, entry.HandlerKey);
                logger.LogTrace("BeforeSend CanDispose={canDispose} for handler {key}: ", entry.CanDispose, entry.HandlerKey);
            }
            return await base.SendAsync(request, cancellationToken);
        }
        finally
        {
            entry.DecrementActiveRequests();
            if (logger.IsEnabled(LogLevel.Trace))
            {
                logger.LogTrace("AfterSend ActiveRequests={count} for handler {key}: ", entry.ActiveRequests, entry.HandlerKey);
                logger.LogTrace("AfterSend CanDispose={canDispose} for handler {key}: ", entry.CanDispose, entry.HandlerKey);
            }
        }

    }

    //

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        entry.IncrementActiveRequests();
        try
        {
            if (logger.IsEnabled(LogLevel.Trace))
            {
                logger.LogTrace("BeforeSend ActiveRequests={count} for handler {key}: ", entry.ActiveRequests, entry.HandlerKey);
                logger.LogTrace("BeforeSend CanDispose={canDispose} for handler {key}: ", entry.CanDispose, entry.HandlerKey);
            }
            return base.Send(request, cancellationToken);
        }
        finally
        {
            entry.DecrementActiveRequests();
            if (logger.IsEnabled(LogLevel.Trace))
            {
                logger.LogTrace("AfterSend ActiveRequests={count} for handler {key}: ", entry.ActiveRequests, entry.HandlerKey);
                logger.LogTrace("AfterSend CanDispose={canDispose} for handler {key}: ", entry.CanDispose, entry.HandlerKey);
            }
        }
    }
}