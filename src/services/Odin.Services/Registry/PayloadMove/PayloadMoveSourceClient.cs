using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Serialization;

#nullable enable

namespace Odin.Services.Registry.PayloadMove;

public enum FetchResult
{
    Fetched,

    /// <summary>The source does not have it (or will not serve it): a failure, not something to retry.</summary>
    NotFound,

    /// <summary>The source asked us to slow down (429, 503).</summary>
    Throttled,

    /// <summary>The source could not be reached or failed: worth retrying.</summary>
    Unavailable
}

public readonly record struct FetchOutcome(FetchResult Result, TimeSpan? RetryAfter = null, string? Error = null);

/// <summary>The target's view of the source's payload move endpoint.</summary>
public interface IPayloadMoveSourceClient
{
    /// <summary>The credential, or <see cref="FetchResult.NotFound"/> if the source refused the token.</summary>
    Task<(FetchOutcome outcome, string? credential)> RedeemAsync(string handoffToken, CancellationToken cancellationToken);

    /// <summary>Copies the object's bytes to <paramref name="destination"/> when the result is Fetched.</summary>
    Task<FetchOutcome> FetchAsync(PayloadObject payloadObject, string credential, Stream destination, CancellationToken cancellationToken);

    Task<FetchOutcome> CompleteAsync(string credential, CancellationToken cancellationToken);
}

/// <summary>
/// Talks to the source over HTTPS. Each call has its own timeout, and a timeout or a network error is
/// <see cref="FetchResult.Unavailable"/> rather than an exception, so it is retried instead of surfacing to
/// the job runner as a cancellation.
/// </summary>
public class HttpPayloadMoveSourceClient(HttpClient client, string baseUrl, Guid identityId) : IPayloadMoveSourceClient
{
    public static readonly TimeSpan ObjectTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CallTimeout = TimeSpan.FromMinutes(1);

    public async Task<(FetchOutcome outcome, string? credential)> RedeemAsync(string handoffToken, CancellationToken cancellationToken)
    {
        var body = OdinSystemSerializer.Serialize(new PayloadMoveRedeemRequest { HandoffToken = handoffToken });
        var request = new HttpRequestMessage(HttpMethod.Post, Url($"{PayloadMoveProtocol.IdentityPath(identityId)}/redeem"))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        string? credential = null;
        var outcome = await SendAsync(request, CallTimeout, async response =>
        {
            var redeemed = OdinSystemSerializer.Deserialize<PayloadMoveRedeemResponse>(await response.Content.ReadAsStringAsync());
            credential = redeemed?.Credential;
        }, cancellationToken);

        return (outcome.Result == FetchResult.Fetched && string.IsNullOrEmpty(credential)
            ? new FetchOutcome(FetchResult.Unavailable, Error: "the source answered without a credential")
            : outcome, credential);
    }

    public Task<FetchOutcome> FetchAsync(PayloadObject payloadObject, string credential, Stream destination, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Url(payloadObject.SourcePath(identityId)));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        return SendAsync(request, ObjectTimeout,
            async response => await response.Content.CopyToAsync(destination, cancellationToken), cancellationToken);
    }

    public Task<FetchOutcome> CompleteAsync(string credential, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Url($"{PayloadMoveProtocol.IdentityPath(identityId)}/complete"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        return SendAsync(request, CallTimeout, _ => Task.CompletedTask, cancellationToken);
    }

    //

    private Uri Url(string path) => new(new Uri(baseUrl), path);

    private async Task<FetchOutcome> SendAsync(HttpRequestMessage request, TimeSpan timeout, Func<HttpResponseMessage, Task> onOk,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            using (request)
            using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token))
            {
                switch (response.StatusCode)
                {
                    case HttpStatusCode.OK:
                        await onOk(response);
                        return new FetchOutcome(FetchResult.Fetched);
                    case HttpStatusCode.NotFound:
                        return new FetchOutcome(FetchResult.NotFound);
                    case HttpStatusCode.TooManyRequests:
                    case HttpStatusCode.ServiceUnavailable:
                        return new FetchOutcome(FetchResult.Throttled, response.Headers.RetryAfter?.Delta);
                    default:
                        return new FetchOutcome(FetchResult.Unavailable, response.Headers.RetryAfter?.Delta,
                            $"the source answered {(int)response.StatusCode}");
                }
            }
        }
        catch (Exception e) when (e is HttpRequestException or IOException ||
                                  (e is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return new FetchOutcome(FetchResult.Unavailable, Error: e is OperationCanceledException ? "timed out" : e.Message);
        }
    }
}
