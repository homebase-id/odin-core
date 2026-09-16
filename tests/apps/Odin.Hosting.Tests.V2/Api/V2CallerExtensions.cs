using Odin.Hosting.Tests._Universal.ApiClient.Factory;

namespace Odin.Hosting.Tests.V2.Api;

/// <summary>
/// Surface that applies to every caller kind but does not belong on <see cref="IV2Caller"/> itself.
/// </summary>
public static class V2CallerExtensions
{
    /// <summary>
    /// A raw Refit client bound to this caller, for fixtures whose system under test is the response
    /// itself — a refusal, an exact status code, a body the typed wrappers swallow.
    /// </summary>
    /// <remarks>
    /// <see cref="OwnerSession"/> has had its own <c>RefitFor&lt;T&gt;</c> since the first batch, built
    /// from the admin client. This is the same idea for App and Guest callers, which previously had to
    /// hand-roll it: two ported fixtures each grew a private <c>CallerRequests(IV2Caller)</c> helper for
    /// exactly this reason. The caller's own factory supplies the token and shared secret, so an App
    /// caller's identity can never be paired with an Owner's credentials.
    /// </remarks>
    public static T RefitFor<T>(this IV2Caller caller)
    {
        var client = caller.Factory.CreateHttpClient(caller.Identity, out var sharedSecret);
        return RefitCreator.RestServiceFor<T>(client, sharedSecret);
    }
}
