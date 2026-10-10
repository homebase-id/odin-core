using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
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

        // The test server has no payload move endpoint, so any answer is a 404; what matters is that there is one
        var first = await source.CompleteAsync("credential", CancellationToken.None);
        Assert.That(first.Result, Is.EqualTo(FetchResult.NotFound), first.Error);

        await WaitUntilEveryHandlerIsDisposedAsync(factory);

        var second = await source.CompleteAsync("credential", CancellationToken.None);
        Assert.That(second.Result, Is.EqualTo(FetchResult.NotFound), second.Error);
    }

    // #1868: accept-missing asks the source whether it has an object now, without fetching it
    [Test]
    public async Task AnObjectTheSourceDoesNotHaveIsNotFound()
    {
        await using var server = new SimpleWebServer();
        using var factory = new DynamicHttpClientFactory(NullLogger<DynamicHttpClientFactory>.Instance);
        var source = new HttpPayloadMoveSourceClient(factory, server.BaseUrl, Guid.NewGuid());

        var outcome = await source.ExistsAsync(new PayloadObject(Guid.NewGuid(), Guid.NewGuid(), "pay_key1",
            new Odin.Core.Time.UnixTimeUtcUnique(1), 0), "credential", CancellationToken.None);

        Assert.That(outcome.Result, Is.EqualTo(FetchResult.NotFound), outcome.Error);
    }

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
