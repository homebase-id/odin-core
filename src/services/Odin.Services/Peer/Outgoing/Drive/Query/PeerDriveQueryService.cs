using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Apps;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Base.SharedTypes;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Incoming.Drive.Query;
using Refit;

namespace Odin.Services.Peer.Outgoing.Drive.Query;

/// <summary>
/// Executes query functionality on connected identity hosts
/// </summary>
public class PeerDriveQueryService(
    ILogger<PeerDriveQueryService> logger,
    IOdinHttpClientFactory odinHttpClientFactory,
    CircleNetworkService circleNetworkService,
    OdinConfiguration odinConfiguration)
{
    public async Task<QueryModifiedResult> GetModified(OdinId odinId, QueryModifiedRequest request, FileSystemType fileSystemType, IOdinContext odinContext, IdentityDatabase db)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.UseTransitRead);

        try
        {
            var (icr, httpClient) = await CreateClient(odinId, fileSystemType, odinContext, db);
            ApiResponse<QueryModifiedResponse> queryModifiedResponse = null;

            await TryRetry.WithDelayAsync(
                odinConfiguration.Host.PeerOperationMaxAttempts,
                odinConfiguration.Host.PeerOperationDelayMs,
                CancellationToken.None,
                async () => { queryModifiedResponse = await httpClient.QueryModified(request); });

            await HandleInvalidResponse(odinId, queryModifiedResponse, odinContext, db);

            var response = queryModifiedResponse.Content;

            return new QueryModifiedResult()
            {
                SearchResults = TransformSharedSecret(response.SearchResults, icr, odinContext),
                Cursor = response.Cursor,
                IncludeHeaderContent = response.IncludeHeaderContent
            };
        }
        catch (TryRetryException t)
        {
            HandleTryRetryException(t);
            throw;
        }
    }

    public async Task<QueryBatchCollectionResponse> GetBatchCollection(OdinId odinId, QueryBatchCollectionRequest request, FileSystemType fileSystemType,
        IOdinContext odinContext, IdentityDatabase db)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.UseTransitRead);

        var (_, httpClient) = await CreateClient(odinId, fileSystemType, odinContext, db);
        try
        {
            ApiResponse<QueryBatchCollectionResponse> queryBatchResponse = null;

            await TryRetry.WithDelayAsync(
                odinConfiguration.Host.PeerOperationMaxAttempts,
                odinConfiguration.Host.PeerOperationDelayMs,
                CancellationToken.None,
                async () => { queryBatchResponse = await httpClient.QueryBatchCollection(request); });

            await HandleInvalidResponse(odinId, queryBatchResponse, odinContext, db);

            var batch = queryBatchResponse.Content;
            return batch;
        }
        catch (TryRetryException t)
        {
            HandleTryRetryException(t);
            throw;
        }
    }

    public async Task<QueryBatchResult> GetBatch(OdinId odinId, QueryBatchRequest request, FileSystemType fileSystemType, IOdinContext odinContext, IdentityDatabase db)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.UseTransitRead);
        var (icr, httpClient) = await CreateClient(odinId, fileSystemType, odinContext, db);

        try
        {
            ApiResponse<QueryBatchResponse> queryBatchResponse = null;

            await TryRetry.WithDelayAsync(
                odinConfiguration.Host.PeerOperationMaxAttempts,
                odinConfiguration.Host.PeerOperationDelayMs,
                CancellationToken.None,
                async () => { queryBatchResponse = await httpClient.QueryBatch(request); });

            await HandleInvalidResponse(odinId, queryBatchResponse, odinContext, db);

            var batch = queryBatchResponse.Content;
            return new QueryBatchResult()
            {
                QueryTime = batch.QueryTime,
                SearchResults = TransformSharedSecret(batch.SearchResults, icr, odinContext),
                Cursor = new QueryBatchCursor(batch.CursorState),
                IncludeMetadataHeader = batch.IncludeMetadataHeader
            };
        }
        catch (TryRetryException t)
        {
            HandleTryRetryException(t);
            throw;
        }
    }

    public async Task<SharedSecretEncryptedFileHeader> GetFileHeader(OdinId odinId, ExternalFileIdentifier file, FileSystemType fileSystemType,
        IOdinContext odinContext, IdentityDatabase db)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.UseTransitRead);

        var (icr, httpClient) = await CreateClient(odinId, fileSystemType, odinContext, db);

        try
        {
            ApiResponse<SharedSecretEncryptedFileHeader> response = null;
            await TryRetry.WithDelayAsync(
                odinConfiguration.Host.PeerOperationMaxAttempts,
                odinConfiguration.Host.PeerOperationDelayMs,
                CancellationToken.None,
                async () => { response = await httpClient.GetFileHeader(file); });

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            await HandleInvalidResponse(odinId, response, odinContext, db);

            var header = TransformSharedSecret(response.Content, icr, odinContext);
            return header;
        }
        catch (TryRetryException t)
        {
            HandleTryRetryException(t);
            throw;
        }
    }

    public async Task<(EncryptedKeyHeader encryptedKeyHeader, bool payloadIsEncrypted, PayloadStream payloadStream)> GetPayloadStream(OdinId odinId,
        ExternalFileIdentifier file, string key, FileChunk chunk, FileSystemType fileSystemType, IOdinContext odinContext, IdentityDatabase db)
    {
        var permissionContext = odinContext.PermissionsContext;
        permissionContext.AssertHasPermission(PermissionKeys.UseTransitRead);

        var (icr, httpClient) = await CreateClient(odinId, fileSystemType, odinContext, db);
        try
        {
            ApiResponse<HttpContent> response = null;
            await TryRetry.WithDelayAsync(
                odinConfiguration.Host.PeerOperationMaxAttempts,
                odinConfiguration.Host.PeerOperationDelayMs,
                CancellationToken.None,
                async () => { response = await httpClient.GetPayloadStream(new GetPayloadRequest() { File = file, Key = key, Chunk = chunk }); });

            return await HandlePayloadResponse(odinId, icr, key, response, odinContext, db);
        }
        catch (TryRetryException t)
        {
            HandleTryRetryException(t);
            throw;
        }
    }

    public async Task<(
        EncryptedKeyHeader ownerSharedSecretEncryptedKeyHeader,
        bool payloadIsEncrypted,
        string decryptedContentType,
        UnixTimeUtc? lastModified,
        Stream thumbnail)> GetThumbnail(OdinId odinId, ExternalFileIdentifier file, int width, int height, string payloadKey, FileSystemType fileSystemType,
        IOdinContext odinContext, IdentityDatabase db)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.UseTransitRead);

        var (icr, httpClient) = await CreateClient(odinId, fileSystemType, odinContext, db);
        try
        {
            ApiResponse<HttpContent> response = null;
            await TryRetry.WithDelayAsync(
                odinConfiguration.Host.PeerOperationMaxAttempts,
                odinConfiguration.Host.PeerOperationDelayMs,
                CancellationToken.None,
                async () =>
                {
                    response = await httpClient.GetThumbnailStream(new GetThumbnailRequest()
                    {
                        File = file,
                        Width = width,
                        Height = height,
                        PayloadKey = payloadKey
                    });
                });

            return await HandleThumbnailResponse(odinId, icr, response, odinContext, db);
        }
        catch (TryRetryException t)
        {
            HandleTryRetryException(t);
            throw;
        }
    }

    public async Task<IEnumerable<PerimeterDriveData>> GetDrivesByType(OdinId odinId, Guid driveType, FileSystemType fileSystemType, IOdinContext odinContext, IdentityDatabase db)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.UseTransitRead);

        var (_, httpClient) = await CreateClient(odinId, fileSystemType, odinContext, db);

        try
        {
            ApiResponse<IEnumerable<PerimeterDriveData>> response = null;
            await TryRetry.WithDelayAsync(
                odinConfiguration.Host.PeerOperationMaxAttempts,
                odinConfiguration.Host.PeerOperationDelayMs,
                CancellationToken.None,
                async () =>
                {
                    response = await httpClient.GetDrives(new GetDrivesByTypeRequest()
                    {
                        DriveType = driveType
                    });
                });

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            await HandleInvalidResponse(odinId, response, odinContext, db);
            return response.Content;
        }
        catch (TryRetryException t)
        {
            HandleTryRetryException(t);
            throw;
        }
    }

    public async Task<SharedSecretEncryptedFileHeader> GetFileHeaderByGlobalTransitId(OdinId odinId, GlobalTransitIdFileIdentifier file,
        FileSystemType fileSystemType, IOdinContext odinContext, IdentityDatabase db)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.UseTransitRead);

        var (icr, httpClient) = await CreateClient(odinId, fileSystemType, odinContext, db);

        try
        {
            ApiResponse<SharedSecretEncryptedFileHeader> response = null;
            await TryRetry.WithDelayAsync(
                odinConfiguration.Host.PeerOperationMaxAttempts,
                odinConfiguration.Host.PeerOperationDelayMs,
                CancellationToken.None,
                async () => { response = await httpClient.GetFileHeaderByGlobalTransitId(file); });

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            await HandleInvalidResponse(odinId, response, odinContext, db);

            var header = TransformSharedSecret(response.Content, icr, odinContext);
            return header;
        }
        catch (TryRetryException t)
        {
            HandleTryRetryException(t);
            throw;
        }
    }

    public async Task<(EncryptedKeyHeader encryptedKeyHeader, bool payloadIsEncrypted, PayloadStream payloadStream)> GetPayloadByGlobalTransitId(OdinId odinId,
        GlobalTransitIdFileIdentifier file, string key,
        FileChunk chunk, FileSystemType fileSystemType, IOdinContext odinContext, IdentityDatabase db)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.UseTransitRead);

        var (icr, httpClient) = await CreateClient(odinId, fileSystemType, odinContext, db);
        try
        {
            ApiResponse<HttpContent> response = null;
            await TryRetry.WithDelayAsync(
                odinConfiguration.Host.PeerOperationMaxAttempts,
                odinConfiguration.Host.PeerOperationDelayMs,
                CancellationToken.None,
                async () =>
                {
                    response = await httpClient.GetPayloadStreamByGlobalTransitId(new GetPayloadByGlobalTransitIdRequest()
                    {
                        File = file,
                        Key = key,
                        Chunk = chunk
                    });
                });

            return await HandlePayloadResponse(odinId, icr, key, response, odinContext, db);
        }
        catch (TryRetryException t)
        {
            HandleTryRetryException(t);
            throw;
        }
    }

    public async Task<(
            EncryptedKeyHeader ownerSharedSecretEncryptedKeyHeader,
            bool payloadIsEncrypted,
            string decryptedContentType,
            UnixTimeUtc? lastModified,
            Stream thumbnail)>
        GetThumbnailByGlobalTransitId(OdinId odinId, GlobalTransitIdFileIdentifier file, string payloadKey,
            int width, int height, bool directMatchOnly, FileSystemType fileSystemType, IOdinContext odinContext, IdentityDatabase db)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.UseTransitRead);

        var (icr, httpClient) = await CreateClient(odinId, fileSystemType, odinContext, db);
        try
        {
            ApiResponse<HttpContent> response = null;
            await TryRetry.WithDelayAsync(
                odinConfiguration.Host.PeerOperationMaxAttempts,
                odinConfiguration.Host.PeerOperationDelayMs,
                CancellationToken.None,
                async () =>
                {
                    response = await httpClient.GetThumbnailStreamByGlobalTransitId(new GetThumbnailByGlobalTransitIdRequest()
                    {
                        File = file,
                        PayloadKey = payloadKey,
                        Width = width,
                        Height = height,
                        DirectMatchOnly = directMatchOnly
                    });
                });

            return await HandleThumbnailResponse(odinId, icr, response, odinContext, db);
        }
        catch (TryRetryException t)
        {
            HandleTryRetryException(t);
            throw;
        }
    }

    public async Task<RedactedOdinContext> GetRemoteDotYouContext(OdinId odinId, IOdinContext odinContext, IdentityDatabase db)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.UseTransitRead);
        try
        {
            var (_, httpClient) = await CreateClient(odinId, null, odinContext, db);

            ApiResponse<RedactedOdinContext> response = null;
            await TryRetry.WithDelayAsync(
                odinConfiguration.Host.PeerOperationMaxAttempts,
                odinConfiguration.Host.PeerOperationDelayMs,
                CancellationToken.None,
                async () => { response = await httpClient.GetRemoteDotYouContext(); });

            await HandleInvalidResponse(odinId, response, odinContext, db);

            return response.Content;
        }
        catch (TryRetryException t)
        {
            HandleTryRetryException(t);
            throw;
        }
    }

    private async Task<(IdentityConnectionRegistration, IPeerDriveQueryHttpClient)> CreateClient(OdinId odinId, FileSystemType? fileSystemType,
        IOdinContext odinContext, IdentityDatabase db)
    {
        //TODO: this check is duplicated in the ResolveClientAccessToken method; need to centralize
        odinContext.PermissionsContext.AssertHasAtLeastOnePermission(
            PermissionKeys.UseTransitWrite,
            PermissionKeys.UseTransitRead);

        //Note here we override the permission check because we have either UseTransitWrite or UseTransitRead
        var icr = await circleNetworkService.GetIdentityConnectionRegistrationAsync(odinId, odinContext, overrideHack: true);
        var authToken = icr.IsConnected() ? icr.CreateClientAuthToken(odinContext.PermissionsContext.GetIcrKey()) : null;
        if (authToken == null)
        {
            var httpClient = odinHttpClientFactory.CreateClient<IPeerDriveQueryHttpClient>(odinId, fileSystemType);
            return (icr, httpClient);
        }
        else
        {
            var httpClient = odinHttpClientFactory.CreateClientUsingAccessToken<IPeerDriveQueryHttpClient>(odinId, authToken, fileSystemType);
            return (icr, httpClient);
        }
    }

    private List<SharedSecretEncryptedFileHeader> TransformSharedSecret(IEnumerable<SharedSecretEncryptedFileHeader> headers,
        IdentityConnectionRegistration icr, IOdinContext odinContext)
    {
        var result = new List<SharedSecretEncryptedFileHeader>();
        foreach (var clientFileHeader in headers)
        {
            result.Add(TransformSharedSecret(clientFileHeader, icr, odinContext));
        }

        return result;
    }

    /// <summary>
    /// Converts the icr-shared-secret-encrypted key header to an owner-shared-secret encrypted key header
    /// </summary>
    private SharedSecretEncryptedFileHeader TransformSharedSecret(SharedSecretEncryptedFileHeader sharedSecretEncryptedFileHeader,
        IdentityConnectionRegistration icr, IOdinContext odinContext)
    {
        EncryptedKeyHeader ownerSharedSecretEncryptedKeyHeader;
        if (sharedSecretEncryptedFileHeader.FileMetadata.IsEncrypted)
        {
            var currentKey = icr.CreateClientAccessToken(odinContext.PermissionsContext.GetIcrKey()).SharedSecret;
            var icrEncryptedKeyHeader = sharedSecretEncryptedFileHeader.SharedSecretEncryptedKeyHeader;
            ownerSharedSecretEncryptedKeyHeader = ReEncrypt(currentKey, icrEncryptedKeyHeader, odinContext);
        }
        else
        {
            ownerSharedSecretEncryptedKeyHeader = EncryptedKeyHeader.Empty();
        }

        sharedSecretEncryptedFileHeader.SharedSecretEncryptedKeyHeader = ownerSharedSecretEncryptedKeyHeader;

        return sharedSecretEncryptedFileHeader;
    }

    private EncryptedKeyHeader ReEncrypt(SensitiveByteArray currentKey, EncryptedKeyHeader encryptedKeyHeader, IOdinContext odinContext)
    {
        var newKey = odinContext.PermissionsContext.SharedSecretKey;
        var keyHeader = encryptedKeyHeader.DecryptAesToKeyHeader(ref currentKey);

        var newEncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, keyHeader.Iv, ref newKey);
        keyHeader.AesKey.Wipe();

        return newEncryptedKeyHeader;
    }

    private async Task HandleInvalidResponse<T>(OdinId odinId, ApiResponse<T> response, IOdinContext odinContext, IdentityDatabase db)
    {
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            if (response.Headers.TryGetValues(HttpHeaderConstants.RemoteServerIcrIssue, out var values))
            {
                var icrIssueHeaderExists = bool.TryParse(values.SingleOrDefault() ?? bool.FalseString, out var isIcrIssue);
                if (icrIssueHeaderExists && isIcrIssue)
                {
                    await circleNetworkService.RevokeConnectionAsync(odinId, odinContext);
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
            throw new OdinSystemException($"Unhandled peer error response: {response.StatusCode}");
        }
    }

    private async Task<(
            EncryptedKeyHeader ownerSharedSecretEncryptedKeyHeader,
            bool payloadIsEncrypted,
            string decryptedContentType,
            UnixTimeUtc? lastModified,
            Stream thumbnail)>
        HandleThumbnailResponse(OdinId odinId, IdentityConnectionRegistration icr, ApiResponse<HttpContent> response, IOdinContext odinContext, IdentityDatabase db)
    {
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return (null, default, null, null, Stream.Null);
        }

        await HandleInvalidResponse(odinId, response, odinContext, db);

        var decryptedContentType = response.Headers.GetValues(HttpHeaderConstants.DecryptedContentType).Single();
        var payloadIsEncrypted = bool.Parse(response.Headers.GetValues(HttpHeaderConstants.PayloadEncrypted).Single());

        if (!DriveFileUtility.TryParseLastModifiedHeader(response.ContentHeaders, out var lastModified))
        {
            logger.LogWarning($"Could not parse remote server response last modified for thumbnail");
        }

        EncryptedKeyHeader sharedSecretEncryptedKeyHeader;
        if (payloadIsEncrypted)
        {
            var ssHeader = response.Headers.GetValues(HttpHeaderConstants.IcrEncryptedSharedSecret64Header).Single();
            var icrEncryptedKeyHeader = EncryptedKeyHeader.FromBase64(ssHeader);
            sharedSecretEncryptedKeyHeader = ReEncrypt(
                icr.CreateClientAccessToken(odinContext.PermissionsContext.GetIcrKey()).SharedSecret,
                icrEncryptedKeyHeader, odinContext);
        }
        else
        {
            sharedSecretEncryptedKeyHeader = EncryptedKeyHeader.Empty();
        }

        var stream = await response!.Content!.ReadAsStreamAsync();

        return (sharedSecretEncryptedKeyHeader, payloadIsEncrypted, decryptedContentType, lastModified, stream);
    }


    private async Task<(EncryptedKeyHeader encryptedKeyHeader, bool payloadIsEncrypted, PayloadStream payloadStream)> HandlePayloadResponse(
        OdinId odinId, IdentityConnectionRegistration icr, string key, ApiResponse<HttpContent> response, IOdinContext odinContext, IdentityDatabase db)
    {
        var permissionContext = odinContext.PermissionsContext;

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return (null, default, null);
        }

        await HandleInvalidResponse(odinId, response, odinContext, db);

        var decryptedContentType = response.Headers.GetValues(HttpHeaderConstants.DecryptedContentType).Single();
        var payloadIsEncrypted = bool.Parse(response.Headers.GetValues(HttpHeaderConstants.PayloadEncrypted).Single());

        if (!DriveFileUtility.TryParseLastModifiedHeader(response.ContentHeaders, out var lastModified))
        {
            logger.LogWarning($"Could not parse last modified for payload (key:{key})");
        }

        EncryptedKeyHeader ownerSharedSecretEncryptedKeyHeader;
        if (payloadIsEncrypted)
        {
            var ssHeader = response.Headers.GetValues(HttpHeaderConstants.IcrEncryptedSharedSecret64Header).Single();

            var icrEncryptedKeyHeader = EncryptedKeyHeader.FromBase64(ssHeader);
            ownerSharedSecretEncryptedKeyHeader = ReEncrypt(
                icr.CreateClientAccessToken(permissionContext.GetIcrKey()).SharedSecret,
                icrEncryptedKeyHeader, odinContext);
        }
        else
        {
            ownerSharedSecretEncryptedKeyHeader = EncryptedKeyHeader.Empty();
        }
        
        var contentLength = response.Content?.Headers.ContentLength ?? throw new OdinSystemException("Missing Content-Length header");

        var stream = await response.Content!.ReadAsStreamAsync();
        var payloadStream = new PayloadStream(key, decryptedContentType, contentLength, lastModified.GetValueOrDefault(UnixTimeUtc.Now()), stream);
        return (ownerSharedSecretEncryptedKeyHeader, payloadIsEncrypted, payloadStream);
    }

    private void HandleTryRetryException(TryRetryException ex)
    {
        var e = ex.InnerException;
        if (e is TaskCanceledException || e is HttpRequestException || e is OperationCanceledException)
        {
            throw new OdinClientException("Failed while calling remote identity", e);
        }
    }
}