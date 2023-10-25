using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.FileSystem.Base;
using Odin.Core.Services.Membership.Connections;
using Odin.Core.Services.Peer.Encryption;
using Odin.Core.Services.Peer.ReceivingHost.Quarantine;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite.DriveDatabase;
using Odin.Core.Time;
using Refit;
using Serilog;

namespace Odin.Core.Services.Peer.SendingHost;

/// <summary>
/// Executes query functionality on connected identity hosts
/// </summary>
public class TransitQueryService
{
    private readonly IOdinHttpClientFactory _odinHttpClientFactory;
    private readonly CircleNetworkService _circleNetworkService;
    private readonly OdinContextAccessor _contextAccessor;

    public TransitQueryService(IOdinHttpClientFactory odinHttpClientFactory, CircleNetworkService circleNetworkService,
        OdinContextAccessor contextAccessor)
    {
        _odinHttpClientFactory = odinHttpClientFactory;
        _circleNetworkService = circleNetworkService;
        _contextAccessor = contextAccessor;
    }


    public async Task<QueryModifiedResult> GetModified(OdinId odinId, QueryModifiedRequest request, FileSystemType fileSystemType)
    {
        _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.UseTransitRead);

        var (icr, httpClient) = await CreateClient(odinId, fileSystemType);
        var queryBatchResponse = await httpClient.QueryModified(request);

        HandleInvalidTransitResponse(odinId, queryBatchResponse);

        var response = queryBatchResponse.Content;

        return new QueryModifiedResult()
        {
            SearchResults = TransformSharedSecret(response.SearchResults, icr),
            Cursor = response.Cursor,
            IncludesJsonContent = response.IncludesJsonContent
        };
    }

    public async Task<QueryBatchCollectionResponse> GetBatchCollection(OdinId odinId, QueryBatchCollectionRequest request, FileSystemType fileSystemType)
    {
        _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.UseTransitRead);

        var (icr, httpClient) = await CreateClient(odinId, fileSystemType);
        var queryBatchResponse = await httpClient.QueryBatchCollection(request);

        HandleInvalidTransitResponse(odinId, queryBatchResponse);

        var batch = queryBatchResponse.Content;
        return batch;
    }


    public async Task<QueryBatchResult> GetBatch(OdinId odinId, QueryBatchRequest request, FileSystemType fileSystemType)
    {
        _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.UseTransitRead);

        var (icr, httpClient) = await CreateClient(odinId, fileSystemType);
        var queryBatchResponse = await httpClient.QueryBatch(request);

        HandleInvalidTransitResponse(odinId, queryBatchResponse);

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
        _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.UseTransitRead);

        var (icr, httpClient) = await CreateClient(odinId, fileSystemType);
        var response = await httpClient.GetFileHeader(file);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        HandleInvalidTransitResponse(odinId, response);

        var header = TransformSharedSecret(response.Content, icr);
        return header;
    }

    public async Task<(EncryptedKeyHeader ownerSharedSecretEncryptedKeyHeader, bool payloadIsEncrypted,
        PayloadStream payloadStream)> GetPayloadStream(OdinId odinId, ExternalFileIdentifier file,
        string key, FileChunk chunk, FileSystemType fileSystemType)
    {
        var permissionContext = _contextAccessor.GetCurrent().PermissionsContext;
        permissionContext.AssertHasPermission(PermissionKeys.UseTransitRead);

        var (icr, httpClient) = await CreateClient(odinId, fileSystemType);
        var response = await httpClient.GetPayloadStream(new GetPayloadRequest() { File = file, Key = key, Chunk = chunk });

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return (null, default, null);
        }

        HandleInvalidTransitResponse(odinId, response);

        var decryptedContentType = response.Headers.GetValues(HttpHeaderConstants.DecryptedContentType).Single();
        var ssHeader = response.Headers.GetValues(HttpHeaderConstants.IcrEncryptedSharedSecret64Header).Single();
        var payloadIsEncrypted = bool.Parse(response.Headers.GetValues(HttpHeaderConstants.PayloadEncrypted).Single());

        if (!DriveFileUtility.TryParseLastModifiedHeader(response.Headers, out var lastModified))
        {
            Log.Warning($"Could not parse last modified for payload (key:{key}) with fileId: {file}");
        }

        EncryptedKeyHeader ownerSharedSecretEncryptedKeyHeader;
        if (payloadIsEncrypted)
        {
            var icrEncryptedKeyHeader = EncryptedKeyHeader.FromBase64(ssHeader);
            ownerSharedSecretEncryptedKeyHeader = ReEncrypt(
                icr.CreateClientAccessToken(permissionContext.GetIcrKey()).SharedSecret,
                icrEncryptedKeyHeader);
        }
        else
        {
            ownerSharedSecretEncryptedKeyHeader = EncryptedKeyHeader.Empty();
        }

        var stream = await response.Content!.ReadAsStreamAsync();
        var payloadStream = new PayloadStream(key, decryptedContentType, lastModified.GetValueOrDefault(UnixTimeUtc.Now()), stream);
        return (ownerSharedSecretEncryptedKeyHeader, payloadIsEncrypted, payloadStream);
    }

    public async Task<(EncryptedKeyHeader ownerSharedSecretEncryptedKeyHeader, bool payloadIsEncrypted, string decryptedContentType, UnixTimeUtc? lastModified,
            Stream thumbnail)>
        GetThumbnail(OdinId odinId, ExternalFileIdentifier file, int width, int height, FileSystemType fileSystemType)
    {
        _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.UseTransitRead);

        var (icr, httpClient) = await CreateClient(odinId, fileSystemType);

        var response = await httpClient.GetThumbnailStream(new GetThumbnailRequest()
        {
            File = file,
            Width = width,
            Height = height,
        });

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return (null, default, null, null, Stream.Null);
        }

        HandleInvalidTransitResponse(odinId, response);

        var decryptedContentType = response.Headers.GetValues(HttpHeaderConstants.DecryptedContentType).Single();
        var payloadIsEncrypted = bool.Parse(response.Headers.GetValues(HttpHeaderConstants.PayloadEncrypted).Single());

        if (!DriveFileUtility.TryParseLastModifiedHeader(response.Headers, out var lastModified))
        {
            Log.Warning($"Could not parse last modified for thumbnail (w:{width}x h:{height}) with fileId: {file}");
        }

        EncryptedKeyHeader ownerSharedSecretEncryptedKeyHeader;
        if (payloadIsEncrypted)
        {
            var ssHeader = response.Headers.GetValues(HttpHeaderConstants.IcrEncryptedSharedSecret64Header).Single();
            var icrEncryptedKeyHeader = EncryptedKeyHeader.FromBase64(ssHeader);
            ownerSharedSecretEncryptedKeyHeader =
                ReEncrypt(icr.CreateClientAccessToken(_contextAccessor.GetCurrent().PermissionsContext.GetIcrKey()).SharedSecret, icrEncryptedKeyHeader);
        }
        else
        {
            ownerSharedSecretEncryptedKeyHeader = EncryptedKeyHeader.Empty();
        }

        var stream = await response!.Content!.ReadAsStreamAsync();

        return (ownerSharedSecretEncryptedKeyHeader, payloadIsEncrypted, decryptedContentType, lastModified, stream);
    }

    public async Task<IEnumerable<PerimeterDriveData>> GetDrivesByType(OdinId odinId, Guid driveType, FileSystemType fileSystemType)
    {
        _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.UseTransitRead);

        var (_, httpClient) = await CreateClient(odinId, fileSystemType);
        var response = await httpClient.GetDrives(new GetDrivesByTypeRequest()
        {
            DriveType = driveType
        });

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        HandleInvalidTransitResponse(odinId, response);
        return response.Content;
    }

    public async Task<RedactedOdinContext> GetRemoteDotYouContext(OdinId odinId)
    {
        _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.UseTransitRead);

        var (_, httpClient) = await CreateClient(odinId, null);
        var response = await httpClient.GetRemoteDotYouContext();
        return response.Content;
    }

    private async Task<(IdentityConnectionRegistration, IPeerHostHttpClient)> CreateClient(OdinId odinId, FileSystemType? fileSystemType)
    {
        //TODO: this check is duplicated in the ResolveClientAccessToken method; need to centralize
        _contextAccessor.GetCurrent().PermissionsContext.AssertHasAtLeastOnePermission(
            PermissionKeys.UseTransitWrite,
            PermissionKeys.UseTransitRead);

        //Note here we override the permission check because we have either UseTransitWrite or UseTransitRead
        var icr = await _circleNetworkService.GetIdentityConnectionRegistration(odinId, overrideHack: true);
        var authToken = icr.IsConnected() ? icr.CreateClientAuthToken(_contextAccessor.GetCurrent().PermissionsContext.GetIcrKey()) : null;
        if (authToken == null)
        {
            var httpClient = _odinHttpClientFactory.CreateClient<IPeerHostHttpClient>(odinId, fileSystemType);
            return (icr, httpClient);
        }
        else
        {
            var httpClient = _odinHttpClientFactory.CreateClientUsingAccessToken<IPeerHostHttpClient>(odinId, authToken, fileSystemType);
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
            var currentKey = icr.CreateClientAccessToken(_contextAccessor.GetCurrent().PermissionsContext.GetIcrKey()).SharedSecret;
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

    private void HandleInvalidTransitResponse<T>(OdinId odinId, ApiResponse<T> response)
    {
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            if (response.Headers.TryGetValues(HttpHeaderConstants.RemoteServerIcrIssue, out var values))
            {
                var icrIssueHeaderExists = bool.TryParse(values.SingleOrDefault() ?? bool.FalseString, out var isIcrIssue);
                if (icrIssueHeaderExists && isIcrIssue)
                {
                    _circleNetworkService.RevokeConnection(odinId).GetAwaiter().GetResult();
                }
            }

            throw new OdinSecurityException("Remote server returned 403");
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