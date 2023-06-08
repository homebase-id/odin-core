using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Base;
using Odin.Core.Services.Contacts.Circle.Membership;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.FileSystem.Base;
using Odin.Core.Services.Transit.Encryption;
using Odin.Core.Services.Transit.ReceivingHost.Quarantine;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite.DriveDatabase;
using Refit;

namespace Odin.Core.Services.Transit.SendingHost;

/// <summary>
/// Executes query functionality on connected identity hosts
/// </summary>
public class TransitQueryService
{
    private readonly IOdinHttpClientFactory _odinHttpClientFactory;
    private readonly ICircleNetworkService _circleNetworkService;
    private readonly OdinContextAccessor _contextAccessor;

    public TransitQueryService(IOdinHttpClientFactory odinHttpClientFactory, ICircleNetworkService circleNetworkService,
        OdinContextAccessor contextAccessor)
    {
        _odinHttpClientFactory = odinHttpClientFactory;
        _circleNetworkService = circleNetworkService;
        _contextAccessor = contextAccessor;
    }

    public async Task<QueryBatchResult> GetBatch(OdinId odinId, QueryBatchRequest request, FileSystemType fileSystemType)
    {
        var (icr, httpClient) = await CreateClient(odinId, fileSystemType);
        var queryBatchResponse = await httpClient.QueryBatch(request);

        AssertValidResponse(queryBatchResponse);

        var batch = queryBatchResponse.Content;
        return new QueryBatchResult()
        {
            QueryTime = batch.QueryTime,
            SearchResults = TransformSharedSecret(batch.SearchResults, icr),
            Cursor = new QueryBatchCursor(batch.CursorState),
            IncludeMetadataHeader = batch.IncludeMetadataHeader
        };
    }

    public async Task<SharedSecretEncryptedFileHeader> GetFileHeader(OdinId odinId, ExternalFileIdentifier file, FileSystemType fileSystemType)
    {
        var (icr, httpClient) = await CreateClient(odinId, fileSystemType);
        var response = await httpClient.GetFileHeader(file);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        AssertValidResponse(response);

        var header = TransformSharedSecret(response.Content, icr);
        return header;
    }

    public async
        Task<(EncryptedKeyHeader ownerSharedSecretEncryptedKeyHeader, bool payloadIsEncrypted, string
            decryptedContentType, Stream payload)> GetPayloadStream(OdinId odinId, ExternalFileIdentifier file,
            FileChunk chunk, FileSystemType fileSystemType)
    {
        var (icr, httpClient) = await CreateClient(odinId, fileSystemType);
        var response = await httpClient.GetPayloadStream(new GetPayloadRequest(){  File = file,Chunk = chunk} );

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

    public async Task<(EncryptedKeyHeader ownerSharedSecretEncryptedKeyHeader, bool payloadIsEncrypted, string decryptedContentType, Stream thumbnail)>
        GetThumbnail(OdinId odinId, ExternalFileIdentifier file, int width, int height, FileSystemType fileSystemType)
    {
        var (icr, httpClient) = await CreateClient(odinId, fileSystemType);

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

    public async Task<IEnumerable<PerimeterDriveData>> GetDrivesByType(OdinId odinId, Guid driveType, FileSystemType fileSystemType)
    {
        var (_, httpClient) = await CreateClient(odinId, fileSystemType);
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

    public async Task<RedactedOdinContext> GetRemoteDotYouContext(OdinId odinId)
    {
        var (_, httpClient) = await CreateClient(odinId, null);
        var response = await httpClient.GetRemoteDotYouContext();
        return response.Content;
    }

    /// <summary />
    // public async Task<GetReactionsResponse> GetReactions(OdinId odinId, GetReactionsRequest request)
    // {
    //     var (icr, httpClient) = await CreateClient(odinId);
    //     var response = await httpClient.GetReactions(request.File,
    //         cursor: request.Cursor,
    //         maxCount: request.MaxRecords);
    //
    //     return response.Content;
    // }
    //
    // /// <summary />
    // public async Task<GetReactionCountsResponse> GetReactionCounts(OdinId odinId, GetReactionsRequest request)
    // {
    //     var (icr, httpClient) = await CreateClient(odinId);
    //     var response = await httpClient.GetReactionCountsByFile(request.File);
    //     return response.Content;
    // }
    //
    // public async Task<List<string>> GetReactionsByIdentityAndFile(OdinId odinId, GetReactionsByIdentityRequest request)
    // {
    //     var (icr, httpClient) = await CreateClient(odinId);
    //     var response = await httpClient.GetReactionsByIdentityAndFile(request.Identity,request.File);
    //
    //     return response.Content;
    // }
    private async Task<(IdentityConnectionRegistration, ITransitHostHttpClient)> CreateClient(OdinId odinId, FileSystemType? fileSystemType)
    {
        var icr = await _circleNetworkService.GetIdentityConnectionRegistration(odinId);

        var authToken = icr.IsConnected() ? icr.CreateClientAuthToken() : null;
        if (authToken == null)
        {
            var httpClient = _odinHttpClientFactory.CreateClient<ITransitHostHttpClient>(odinId, fileSystemType);
            return (icr, httpClient);
        }
        else
        {
            var httpClient = _odinHttpClientFactory.CreateClientUsingAccessToken<ITransitHostHttpClient>(odinId, authToken, fileSystemType);
            return (icr, httpClient);
        }
    }

    private List<SharedSecretEncryptedFileHeader> TransformSharedSecret(IEnumerable<SharedSecretEncryptedFileHeader> headers,
        IdentityConnectionRegistration icr)
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
    private SharedSecretEncryptedFileHeader TransformSharedSecret(SharedSecretEncryptedFileHeader sharedSecretEncryptedFileHeader,
        IdentityConnectionRegistration icr)
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
            throw new OdinClientException("Remote server returned 403", OdinClientErrorCode.RemoteServerReturnedForbidden);
        }

        if (response.StatusCode == HttpStatusCode.InternalServerError)
        {
            throw new OdinClientException("Remote server returned 500", OdinClientErrorCode.RemoteServerReturnedInternalServerError);
        }

        if (!response.IsSuccessStatusCode || response.Content == null)
        {
            throw new OdinSystemException($"Unhandled transit error response: {response.StatusCode}");
        }
    }
}