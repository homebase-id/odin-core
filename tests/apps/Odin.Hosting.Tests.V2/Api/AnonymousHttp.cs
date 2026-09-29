#nullable enable
using System;
using System.Net.Http;
using Odin.Core.Storage;
using Odin.Hosting.Tests.V2.Hosting;
using Odin.Services.Base;
using Refit;

namespace Odin.Hosting.Tests.V2.Api;

/// <summary>
/// Unauthenticated HTTP against one tenant on an in-process <see cref="OdinHost"/> — the V2
/// counterpart of V1's <c>WebScaffold.CreateAnonymousApiHttpClient</c>.
/// </summary>
/// <remarks>
/// <see cref="OdinHost.CreateClient"/> alone leaves <c>BaseAddress</c> at TestServer's
/// <c>http://localhost/</c>, so every call has to spell out an absolute URL and no tenant is named.
/// The fixtures that GET a well-known / SSR / swagger path, or point Refit at an anonymous surface,
/// need a client already bound to a tenant, which is what this returns. The file-system-type header
/// is set because the V1 anonymous client set it and some anonymous drive reads resolve on it.
/// </remarks>
public static class AnonymousHttp
{
    public static HttpClient CreateAnonymousClient(
        this OdinHost host,
        string identity,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(OdinHeaderNames.FileSystemTypeHeader, Enum.GetName(fileSystemType));
        client.BaseAddress = new Uri($"https://{identity}/");
        return client;
    }

    /// <summary>
    /// A Refit surface <typeparamref name="T"/> addressed at <paramref name="identity"/> with no
    /// credentials at all.
    /// </summary>
    /// <remarks>
    /// Deliberately <see cref="RestService"/> and not <c>RefitCreator</c>: the latter wraps requests
    /// in shared-secret encryption, and an anonymous caller has no shared secret. This is the
    /// distinction between "a guest" and "no credentials whatsoever" — <see cref="GuestSession"/>
    /// always registers a YouAuth domain and carries a token, so it cannot stand in for a browser
    /// hitting a public drive with no session.
    /// <para>
    /// <typeparamref name="T"/> must declare absolute paths (the guest drive interface does:
    /// <c>/api/guest/v1/...</c>) — this client has no V1 base-path rewriting.
    /// </para>
    /// </remarks>
    public static T AnonymousRefitFor<T>(
        this OdinHost host,
        string identity,
        FileSystemType fileSystemType = FileSystemType.Standard)
        => RestService.For<T>(host.CreateAnonymousClient(identity, fileSystemType));
}
