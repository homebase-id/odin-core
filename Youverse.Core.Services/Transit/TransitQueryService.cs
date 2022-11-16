using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Refit;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Services.Apps;
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
        var (icr, httpClient) = await CreateClient(dotYouId);
        var queryBatchResponse = await httpClient.QueryBatch(request);

        AssertValidResponse(queryBatchResponse);

        var batch = queryBatchResponse.Content;
        return new QueryBatchResult()
        {
            SearchResults = TransformSharedSecret(batch.SearchResults, icr),
            Cursor = new QueryBatchCursor(batch.CursorState),
            IncludeMetadataHeader = batch.IncludeMetadataHeader
        };
    }

    public async Task<ClientFileHeader> GetFileHeader(DotYouIdentity dotYouId, ExternalFileIdentifier file)
    {
        var (icr, httpClient) = await CreateClient(dotYouId);
        var response = await httpClient.GetFileHeader(file);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        AssertValidResponse(response);

        var header = TransformSharedSecret(response.Content, icr);
        return header;
    }

    public async Task<(EncryptedKeyHeader ownerSharedSecretEncryptedKeyHeader, Stream payload)> GetPayloadStream(DotYouIdentity dotYouId, ExternalFileIdentifier file)
    {
        var (icr, httpClient) = await CreateClient(dotYouId);
        var response = await httpClient.GetPayloadStream(file);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return (null, null);
        }

        AssertValidResponse(response);

        response.Headers.TryGetValues(TransitConstants.IcrEncryptedSharedSecret64Header, out IEnumerable<string> values);
        var icrEncryptedKeyHeader = EncryptedKeyHeader.FromBase64(values!.Single());
        var ownerSharedSecretEncryptedKeyHeader = ReEncrypt(icr.ClientAccessTokenSharedSecret.ToSensitiveByteArray(), icrEncryptedKeyHeader);
        var stream = await response!.Content!.ReadAsStreamAsync();

        return (ownerSharedSecretEncryptedKeyHeader, stream);
    }

    public async Task<(EncryptedKeyHeader ownerSharedSecretEncryptedKeyHeader, Stream thumbnail)> GetThumbnail(DotYouIdentity dotYouId, ExternalFileIdentifier file, int width, int height)
    {
        var (icr, httpClient) = await CreateClient(dotYouId);

        var response = await httpClient.GetThumbnail(new GetThumbnailRequest()
        {
            File = file,
            Width = width,
            Height = height,
        });

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return (null, null);
        }

        AssertValidResponse(response);

        response.Headers.TryGetValues(TransitConstants.IcrEncryptedSharedSecret64Header, out IEnumerable<string> values);
        var icrEncryptedKeyHeader = EncryptedKeyHeader.FromBase64(values!.Single());
        var ownerSharedSecretEncryptedKeyHeader = ReEncrypt(icr.ClientAccessTokenSharedSecret.ToSensitiveByteArray(), icrEncryptedKeyHeader);
        var stream = await response!.Content!.ReadAsStreamAsync();

        return (ownerSharedSecretEncryptedKeyHeader, stream);
    }

    private async Task<(IdentityConnectionRegistration, ITransitHostHttpClient)> CreateClient(DotYouIdentity dotYouId)
    {
        var icr = await _circleNetworkService.GetIdentityConnectionRegistration(dotYouId);
        var httpClient = _dotYouHttpClientFactory.CreateClientUsingAccessToken<ITransitHostHttpClient>(dotYouId, icr.CreateClientAuthToken());

        return (icr, httpClient);
    }

    private List<ClientFileHeader> TransformSharedSecret(IEnumerable<ClientFileHeader> headers, IdentityConnectionRegistration icr)
    {
        var result = new List<ClientFileHeader>();
        foreach (var clientFileHeader in headers)
        {
            result.Add(TransformSharedSecret(clientFileHeader, icr));
        }

        return result;
    }

    /// <summary>
    /// Converts the icr-shared-secret-encrypted key header to an owner-shared-secret encrypted key header
    /// </summary>
    /// <param name="clientFileHeader"></param>
    /// <param name="icr"></param>
    private ClientFileHeader TransformSharedSecret(ClientFileHeader clientFileHeader, IdentityConnectionRegistration icr)
    {
        EncryptedKeyHeader ownerSharedSecretEncryptedKeyHeader;
        if (clientFileHeader.FileMetadata.PayloadIsEncrypted)
        {
            var currentKey = icr.ClientAccessTokenSharedSecret.ToSensitiveByteArray();
            var icrEncryptedKeyHeader = clientFileHeader.SharedSecretEncryptedKeyHeader;
            ownerSharedSecretEncryptedKeyHeader = ReEncrypt(currentKey, icrEncryptedKeyHeader);
        }
        else
        {
            ownerSharedSecretEncryptedKeyHeader = EncryptedKeyHeader.Empty();
        }

        clientFileHeader.SharedSecretEncryptedKeyHeader = ownerSharedSecretEncryptedKeyHeader;

        return clientFileHeader;
    }

    private EncryptedKeyHeader ReEncrypt(SensitiveByteArray currentKey, EncryptedKeyHeader encryptedKeyHeader)
    {
        var newKey = _contextAccessor.GetCurrent().PermissionsContext.SharedSecretKey;
        var keyHeader = encryptedKeyHeader.DecryptAesToKeyHeader(ref currentKey);
        var newEncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, encryptedKeyHeader.Iv, ref newKey);
        keyHeader.AesKey.Wipe();

        return newEncryptedKeyHeader;
    }

    private void AssertValidResponse<T>(ApiResponse<T> response)
    {
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new YouverseClientException("Remote server returned 403", YouverseClientErrorCode.RemoteServerReturnedForbidden);
        }

        if (response.StatusCode == HttpStatusCode.InternalServerError)
        {
            throw new YouverseClientException("Remote server returned 500", YouverseClientErrorCode.RemoteServerReturnedInternalServerError);
        }

        if (!response.IsSuccessStatusCode || response.Content == null)
        {
            throw new YouverseSystemException($"Unhandled transit error response: {response.StatusCode}");
        }
    }
}