using System.Net;
using System.Threading.Tasks;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Drive;
using Youverse.Core.Storage.SQLite;
using Youverse.Hosting.Controllers;

namespace Youverse.Core.Services.Transit;

/// <summary>
/// Executes drive query functionality on connected identity hosts
/// </summary>
public class TransitQueryService
{
    private readonly IDotYouHttpClientFactory _dotYouHttpClientFactory;
    private readonly ICircleNetworkService _circleNetworkService;

    public TransitQueryService(IDotYouHttpClientFactory dotYouHttpClientFactory, ICircleNetworkService circleNetworkService)
    {
        _dotYouHttpClientFactory = dotYouHttpClientFactory;
        _circleNetworkService = circleNetworkService;
    }

    public async Task<QueryBatchResult> GetBatch(DotYouIdentity dotYouId, QueryBatchRequest request)
    {
        var clientAuthToken = _circleNetworkService.GetConnectionAuthToken(dotYouId).GetAwaiter().GetResult();
        var httpClient = _dotYouHttpClientFactory.CreateClientUsingAccessToken<ITransitHostHttpClient>(dotYouId, clientAuthToken);

        var queryBatchResponse = await httpClient.QueryBatch(request);

        if (queryBatchResponse.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new YouverseClientException("Remove server returned 403", YouverseClientErrorCode.RemoteServerReturnedForbidden);
        }

        if (queryBatchResponse.StatusCode == HttpStatusCode.InternalServerError)
        {
            throw new YouverseClientException("Remove server returned 500", YouverseClientErrorCode.RemoteServerReturnedInternalServerError);
        }

        if (!queryBatchResponse.IsSuccessStatusCode || queryBatchResponse.Content == null)
        {
            throw new YouverseSystemException($"Unhandled transit error response: {queryBatchResponse.StatusCode}");
        }

        var batch = queryBatchResponse.Content.Batch;

        return new QueryBatchResult()
        {
            SearchResults = batch.SearchResults,
            Cursor = new QueryBatchCursor(batch.CursorState),
            IncludeMetadataHeader = batch.IncludeMetadataHeader
        };
    }
}