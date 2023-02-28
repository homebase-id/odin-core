using System;
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
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Quarantine;
using Youverse.Core.Storage.SQLite;
using Youverse.Core.Storage.SQLite.DriveDatabase;

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

    public async Task<QueryBatchResult> GetBatch(OdinId odinId, QueryBatchRequest request)
    {
        var (icr, httpClient) = await CreateClient(odinId);
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

    public async Task<SharedSecretEncryptedFileHeader> GetFileHeader(OdinId odinId, ExternalFileIdentifier file)
    {
        var (icr, httpClient) = await CreateClient(odinId);
        var response = await httpClient.GetFileHeader(file);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        AssertValidResponse(response);

        var header = TransformSharedSecret(response.Content, icr);
        return header;
    }

    public async Task<(EncryptedKeyHeader ownerSharedSecretEncryptedKeyHeader, bool payloadIsEncrypted, string decryptedContentType, Stream payload)> GetPayloadStream(OdinId odinId,
        ExternalFileIdentifier file)
    {
        var (icr, httpClient) = await CreateClient(odinId);
        var response = await httpClient.GetPayloadStream(file);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return (null, default, null, null);
        }

        AssertValidResponse(response);

        var decryptedContentType = response.Headers.GetValues(HttpHeaderConstants.DecryptedContentType).Single();
        var ssHeader = response.Headers.GetValues(HttpHeaderConstants.IcrEncryptedSharedSecret64Header).Single();
        var payloadIsEncrypted = bool.Parse(response.Headers.GetValues(HttpHeaderConstants.PayloadEncrypted).Single());

        EncryptedKeyHeader ownerSharedSecretEncryptedKeyHeader;
        if (payloadIsEncrypted)
        {
            var icrEncryptedKeyHeader = EncryptedKeyHeader.FromBase64(ssHeader);
            ownerSharedSecretEncryptedKeyHeader = ReEncrypt(icr.ClientAccessTokenSharedSecret.ToSensitiveByteArray(), icrEncryptedKeyHeader);
        }
        else
        {
            ownerSharedSecretEncryptedKeyHeader = EncryptedKeyHeader.Empty();
        }

        var stream = await response!.Content!.ReadAsStreamAsync();

        return (ownerSharedSecretEncryptedKeyHeader, payloadIsEncrypted, decryptedContentType, stream);
    }

    public async Task<(EncryptedKeyHeader ownerSharedSecretEncryptedKeyHeader, bool payloadIsEncrypted, string decryptedContentType, Stream thumbnail)> GetThumbnail(OdinId odinId,
        ExternalFileIdentifier file,
        int width, int height)
    {
        var (icr, httpClient) = await CreateClient(odinId);

        var response = await httpClient.GetThumbnailStream(new GetThumbnailRequest()
        {
            File = file,
            Width = width,
            Height = height,
        });

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return (null, default, null, null);
        }

        AssertValidResponse(response);

        var decryptedContentType = response.Headers.GetValues(HttpHeaderConstants.DecryptedContentType).Single();
        var payloadIsEncrypted = bool.Parse(response.Headers.GetValues(HttpHeaderConstants.PayloadEncrypted).Single());

        EncryptedKeyHeader ownerSharedSecretEncryptedKeyHeader;
        if (payloadIsEncrypted)
        {
            var ssHeader = response.Headers.GetValues(HttpHeaderConstants.IcrEncryptedSharedSecret64Header).Single();
            var icrEncryptedKeyHeader = EncryptedKeyHeader.FromBase64(ssHeader);
            ownerSharedSecretEncryptedKeyHeader = ReEncrypt(icr.ClientAccessTokenSharedSecret.ToSensitiveByteArray(), icrEncryptedKeyHeader);
        }
        else
        {
            ownerSharedSecretEncryptedKeyHeader = EncryptedKeyHeader.Empty();
        }

        var stream = await response!.Content!.ReadAsStreamAsync();

        return (ownerSharedSecretEncryptedKeyHeader, payloadIsEncrypted, decryptedContentType, stream);
    }

    public async Task<IEnumerable<PerimeterDriveData>> GetDrivesByType(OdinId odinId, Guid driveType)
    {
        var (icr, httpClient) = await CreateClient(odinId);
        var response = await httpClient.GetDrives(new GetDrivesByTypeRequest()
        {
            DriveType = driveType
        });

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        AssertValidResponse(response);
        return response.Content;
    }

    private async Task<(IdentityConnectionRegistration, ITransitHostHttpClient)> CreateClient(OdinId odinId)
    {
        var icr = await _circleNetworkService.GetIdentityConnectionRegistration(odinId);
        var httpClient = _dotYouHttpClientFactory.CreateClientUsingAccessToken<ITransitHostHttpClient>(odinId, icr.CreateClientAuthToken());

        return (icr, httpClient);
    }

    private List<SharedSecretEncryptedFileHeader> TransformSharedSecret(IEnumerable<SharedSecretEncryptedFileHeader> headers, IdentityConnectionRegistration icr)
    {
        var result = new List<SharedSecretEncryptedFileHeader>();
        foreach (var clientFileHeader in headers)
        {
            result.Add(TransformSharedSecret(clientFileHeader, icr));
        }

        return result;
    }

    /// <summary>
    /// Converts the icr-shared-secret-encrypted key header to an owner-shared-secret encrypted key header
    /// </summary>
    /// <param name="sharedSecretEncryptedFileHeader"></param>
    /// <param name="icr"></param>
    private SharedSecretEncryptedFileHeader TransformSharedSecret(SharedSecretEncryptedFileHeader sharedSecretEncryptedFileHeader, IdentityConnectionRegistration icr)
    {
        EncryptedKeyHeader ownerSharedSecretEncryptedKeyHeader;
        if (sharedSecretEncryptedFileHeader.FileMetadata.PayloadIsEncrypted)
        {
            var currentKey = icr.ClientAccessTokenSharedSecret.ToSensitiveByteArray();
            var icrEncryptedKeyHeader = sharedSecretEncryptedFileHeader.SharedSecretEncryptedKeyHeader;
            ownerSharedSecretEncryptedKeyHeader = ReEncrypt(currentKey, icrEncryptedKeyHeader);
        }
        else
        {
            ownerSharedSecretEncryptedKeyHeader = EncryptedKeyHeader.Empty();
        }

        sharedSecretEncryptedFileHeader.SharedSecretEncryptedKeyHeader = ownerSharedSecretEncryptedKeyHeader;

        return sharedSecretEncryptedFileHeader;
    }

    private EncryptedKeyHeader ReEncrypt(SensitiveByteArray currentKey, EncryptedKeyHeader encryptedKeyHeader)
    {
        var newKey = _contextAccessor.GetCurrent().PermissionsContext.SharedSecretKey;
        var keyHeader = encryptedKeyHeader.DecryptAesToKeyHeader(ref currentKey);
        var newEncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, keyHeader.Iv, ref newKey);
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