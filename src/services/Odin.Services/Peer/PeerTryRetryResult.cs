using Refit;

namespace Odin.Services.Peer;

public class PeerTryRetryResult<TApiResponse>
{
    public PeerRequestIssueType IssueType { get; set; }

    public ApiResponse<TApiResponse> Response { get; set; }
}