using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Odin.Core.Http;

#nullable enable

internal sealed class RequestTrackingHandler(ILogger logger, HttpMessageHandler innerHandler, HandlerEntry entry)
    : DelegatingHandler(innerHandler)
{
    private volatile bool _disposed;

    //

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        entry.IncrementActiveRequests();
        try
        {
            if (logger.IsEnabled(LogLevel.Trace))
            {
                logger.LogTrace("BeforeSend ActiveRequests={count} for handler {key}: ", entry.ActiveRequests, entry.HandlerKey);
            }
            return await base.SendAsync(request, cancellationToken);
        }
        finally
        {
            entry.DecrementActiveRequests();
            if (logger.IsEnabled(LogLevel.Trace))
            {
                logger.LogTrace("AfterSend ActiveRequests={count} for handler {key}: ", entry.ActiveRequests, entry.HandlerKey);
            }
        }

    }

    //

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

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

    //

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
        }
        base.Dispose(disposing);
    }

    //

}