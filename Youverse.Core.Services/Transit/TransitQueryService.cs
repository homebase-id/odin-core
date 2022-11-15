using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Storage.SQLite;

namespace Youverse.Core.Services.Transit;

/// <summary>
/// Executes drive query functionality on connected identity hosts
/// </summary>
public class TransitQueryService
{
    private readonly IDotYouHttpClientFactory _dotYouHttpClientFactory;
    private readonly ICircleNetworkService _circleNetworkService;
    private readonly DotYouContextAccessor _contextAccessor;

    public TransitQueryService(IDotYouHttpClientFactory dotYouHttpClientFactory, ICircleNetworkService circleNetworkService, DotYouContextAccessor contextAccessor)
    {
        _dotYouHttpClientFactory = dotYouHttpClientFactory;
        _circleNetworkService = circleNetworkService;
        _contextAccessor = contextAccessor;
    }

    public async Task<QueryBatchResult> GetBatch(DotYouIdentity dotYouId, QueryBatchRequest request)
    {
        var icr = await _circleNetworkService.GetIdentityConnectionRegistration(dotYouId);
        var httpClient = _dotYouHttpClientFactory.CreateClientUsingAccessToken<ITransitHostHttpClient>(dotYouId, icr.CreateClientAuthToken());

        var queryBatchResponse = await httpClient.QueryBatch(request);

        if (queryBatchResponse.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new YouverseClientException("Remote server returned 403", YouverseClientErrorCode.RemoteServerReturnedForbidden);
        }

        if (queryBatchResponse.StatusCode == HttpStatusCode.InternalServerError)
        {
            throw new YouverseClientException("Remote server returned 500", YouverseClientErrorCode.RemoteServerReturnedInternalServerError);
        }

        if (!queryBatchResponse.IsSuccessStatusCode || queryBatchResponse.Content == null)
        {
            throw new YouverseSystemException($"Unhandled transit error response: {queryBatchResponse.StatusCode}");
        }

        if (queryBatchResponse.Content.Code != TransitResponseCode.Accepted)
        {
            throw new YouverseClientException("Remote Server Rejected", YouverseClientErrorCode.RemoteServerTransitRejected);
        }

        var batch = queryBatchResponse.Content.Batch;

        //convert the icr-shared-secret-encrypted key header to an owner-shared-secret encrypted key header
        foreach (var result in batch.SearchResults)
        {
            EncryptedKeyHeader ownerSharedSecretEncryptedKeyHeader;
            if (result.FileMetadata.PayloadIsEncrypted)
            {
                var decryptionIcrSharedSecret = icr.ClientAccessTokenSharedSecret.ToSensitiveByteArray();
                var keyHeader = result.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref decryptionIcrSharedSecret);

                var clientSharedSecret = _contextAccessor.GetCurrent().PermissionsContext.SharedSecretKey;
                ownerSharedSecretEncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, result.SharedSecretEncryptedKeyHeader.Iv, ref clientSharedSecret);
            }
            else
            {
                ownerSharedSecretEncryptedKeyHeader = EncryptedKeyHeader.Empty();
            }

            result.SharedSecretEncryptedKeyHeader = ownerSharedSecretEncryptedKeyHeader;

        }

        //
        return new QueryBatchResult()
        {
            SearchResults = batch.SearchResults,
            Cursor = new QueryBatchCursor(batch.CursorState),
            IncludeMetadataHeader = batch.IncludeMetadataHeader
        };
    }
}