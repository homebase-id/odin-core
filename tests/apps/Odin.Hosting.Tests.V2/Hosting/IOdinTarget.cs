#nullable enable
using System;
using System.Net.Http;

namespace Odin.Hosting.Tests.V2.Hosting;

/// <summary>
/// Where the V2 test clients send their requests: an in-process <see cref="OdinHost"/> in a test, or the local
/// <see cref="DevServer"/> for a fixture tool. The only things the clients need to know about either.
/// </summary>
public interface IOdinTarget
{
    /// <summary>A handler for one client, which the client may dispose: what it wraps is the target's, and shared</summary>
    HttpMessageHandler CreateHandler();

    /// <summary>The address <paramref name="identity"/> is reached at</summary>
    Uri BaseFor(string identity);

    ITestSync GetTestSync(string identity);
}

/// <summary>
/// Passes a request straight through to a target's shared handler. Exists so a client's own delegating chain can be
/// disposed by <see cref="HttpClient.Dispose()"/> without taking the shared handler with it
/// (<see cref="DelegatingHandler"/> disposes its inner by default).
/// </summary>
internal sealed class NonDisposingPassthroughHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    // base.Dispose with disposing=false skips inner.Dispose
    protected override void Dispose(bool disposing) => base.Dispose(disposing: false);
}
