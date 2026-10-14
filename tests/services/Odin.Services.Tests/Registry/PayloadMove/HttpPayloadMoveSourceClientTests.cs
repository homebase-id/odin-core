using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Odin.Core.Http;
using Odin.Services.Registry.PayloadMove;
using Odin.Test.Helpers.WebServers;

#nullable enable

namespace Odin.Services.Tests.Registry.PayloadMove;

public class HttpPayloadMoveSourceClientTests
{
    // #1867: a slice runs for minutes, longer than the factory keeps a handler, and a client held for the
    // whole slice failed every call with ObjectDisposedException once the factory disposed its handler
    [Test]
    public async Task KeepsWorkingAfterTheFactoryDisposesTheHandlerItStartedWith()
    {
        await using var server = new SimpleWebServer();
        using var factory = new DynamicHttpClientFactory(
            NullLogger<DynamicHttpClientFactory>.Instance,
            defaultHandlerLifetime: TimeSpan.FromMilliseconds(50),
            cleanupInterval: TimeSpan.FromMilliseconds(20),
            disposeGracePeriod: TimeSpan.FromMilliseconds(50));

        var source = new HttpPayloadMoveSourceClient(factory, server.BaseUrl, Guid.NewGuid());

        // The test server has no payload move endpoint, so any answer is a bare 404; what matters is that there is one
        var first = await source.CompleteAsync("credential", CancellationToken.None);
        Assert.That(first.Result, Is.EqualTo(FetchResult.Refused), first.Error);

        await WaitUntilEveryHandlerIsDisposedAsync(factory);

        var second = await source.CompleteAsync("credential", CancellationToken.None);
        Assert.That(second.Result, Is.EqualTo(FetchResult.Refused), second.Error);
    }

    // #1868: accept-missing asks the source whether it has an object now, without fetching it. #1919: only a 404
    // the source marks as absent is absence; a bare one is a refusal, whatever the reason
    [Test]
    public async Task AnObjectIsMissingOnlyWhenTheSourceSaysSo()
    {
        var present = NewObject();
        var absent = NewObject();
        var requests = new ConcurrentQueue<string>();
        await using var server = new SimpleWebServer(app => app.Map(PayloadMoveProtocol.RootPath + "/{**rest}", (HttpContext context) =>
        {
            requests.Enqueue($"{context.Request.Method} {context.Request.Headers.Authorization}");
            if (context.Request.Headers.Authorization != "Bearer credential")
            {
                return Results.NotFound();
            }

            if (context.Request.Path.Value!.Contains(present.FileId.ToString()))
            {
                return Results.Ok();
            }

            context.Response.Headers[PayloadMoveProtocol.AbsentHeader] = "1";
            return Results.NotFound();
        }));
        using var factory = new DynamicHttpClientFactory(NullLogger<DynamicHttpClientFactory>.Instance);
        var source = new HttpPayloadMoveSourceClient(factory, server.BaseUrl, Guid.NewGuid());

        var there = await source.ExistsAsync(present, "credential", CancellationToken.None);
        var missing = await source.ExistsAsync(absent, "credential", CancellationToken.None);
        var refused = await source.ExistsAsync(absent, "revoked", CancellationToken.None);

        Assert.That((there.Result, missing.Result, refused.Result),
            Is.EqualTo((FetchResult.Fetched, FetchResult.NotFound, FetchResult.Refused)), refused.Error);
        Assert.That(requests, Is.EqualTo(new[] { "HEAD Bearer credential", "HEAD Bearer credential", "HEAD Bearer revoked" }),
            "asked without fetching it");
    }

    private static PayloadObject NewObject() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "pay_key1", new Odin.Core.Time.UnixTimeUtcUnique(1), 0);

    private static async Task WaitUntilEveryHandlerIsDisposedAsync(DynamicHttpClientFactory factory)
    {
        var clock = Stopwatch.StartNew();
        while (factory.CountActiveHandlers() + factory.CountExpiredHandlers() > 0)
        {
            Assert.That(clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)),
                $"the factory still holds {factory.CountActiveHandlers()} active and {factory.CountExpiredHandlers()} expired handler(s)");
            await Task.Delay(20);
        }
    }
}
