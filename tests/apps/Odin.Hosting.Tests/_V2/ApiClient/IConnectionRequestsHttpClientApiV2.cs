using System.Net.Http;
using System.Threading.Tasks;
using Odin.Hosting.UnifiedV2;
using Odin.Services.Membership.Connections.Requests;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public interface IConnectionRequestsHttpClientApiV2
{
    private const string Root = UnifiedApiRouteConstants.Connections;

    [Post(Root + "/requests/auto-connect")]
    Task<ApiResponse<ConnectionRequestResult>> AutoConnect([Body] ConnectionRequestHeader header);

    // The endpoint takes no body, but SharedSecretEncryptionMiddleware only exempts bodyless POSTs
    // from decryption — a PUT must still carry a shared-secret-encrypted payload, so send an empty one.
    [Put(Root + "/requests/incoming/{senderId}")]
    Task<ApiResponse<HttpContent>> AcceptIncomingRequest(string senderId, [Body] object body);

    [Delete(Root + "/requests/outgoing/{recipientId}")]
    Task<ApiResponse<HttpContent>> CancelOutgoingRequest(string recipientId);
}
