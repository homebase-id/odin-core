using System;
using System.Net;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer.Outgoing.Drive.Query;
using Odin.Services.Peer.Outgoing.Drive.Transfer;

namespace Odin.Services.Peer.Outgoing.DataRequestService;

public class DataRequestService(
    IOdinHttpClientFactory odinHttpClientFactory,
    CircleNetworkService cns,
    FileSystemResolver fileSystemResolver,
    OdinConfiguration odinConfiguration,
    OutgoingPeerDriveQueryService service,
    TableNonce nonce) : PeerServiceBase(odinHttpClientFactory, cns, fileSystemResolver, odinConfiguration)
{
    public async Task RequestRemoteFile(OdinId remoteIdentity,
        FileIdentifier file,
        TargetDrive targetDrive,
        FileSystemType fst,
        bool overwrite,
        IOdinContext odinContext)
    {
        file.AssertIsValid(FileIdentifierType.GlobalTransitId);
        odinContext.PermissionsContext.AssertCanWriteToDrive(targetDrive.Alias);

        var nonceId = Guid.NewGuid();
        await nonce.InsertAsync(new NonceRecord()
        {
            id = nonceId,
            expiration = UnixTimeUtc.Now().AddHours(24 * 3),
            data = OdinSystemSerializer.Serialize(new FileRequestOptions()
            {
                ShouldOverwrite = overwrite,
                TargetDrive = targetDrive
            })
        });

        //
        // ship off a request for the file 
        //
        var request = new RemoteFileRequest
        {
            File = file,
            Nonce = nonceId,
            FileSystemType = fst,
            RemoteTargetDrive = targetDrive
        };

        var clientAuthToken = await ResolveClientAccessTokenAsync(remoteIdentity, odinContext);
        var client = OdinHttpClientFactory.CreateClientUsingAccessToken<IPeerTransferHttpClient>(
            remoteIdentity,
            clientAuthToken.ToAuthenticationToken(),
            request.FileSystemType);

        var response = await client.RequestRemoteFile(request);
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                throw new OdinClientException("Invalid file", OdinClientErrorCode.InvalidFile);
            }

            throw new OdinSystemException($"Unhandled response status code {response.StatusCode}");
        }
    }

    public async Task<FileIdentifier> CopyFile(OdinId remoteIdentity,
        FileIdentifier source,
        TargetDrive targetDrive,
        FileSystemType fst,
        bool overwrite,
        IOdinContext odinContext)
    {
        odinContext.PermissionsContext.AssertCanWriteToDrive(targetDrive.Alias);

        var driveId = targetDrive.Alias;
        var file = source.ToGlobalTransitIdFileIdentifier();

        var header = await service.GetFileHeaderByGlobalTransitIdAsync(remoteIdentity,
            file, fst, odinContext);

        var fileSystem = FileSystemResolver.ResolveFileSystem(fst);
        if (null == header)
        {
            throw new OdinClientException("Remote file does not exist", OdinClientErrorCode.InvalidFile);
        }

        var existingFile = await fileSystem.Query.GetFileByGlobalTransitId(driveId,
            source.GlobalTransitId.GetValueOrDefault(),
            odinContext);

        if (existingFile != null && !overwrite)
        {
            throw new OdinClientException("File exists on target drive; overwrite is set to false",
                OdinClientErrorCode.IdAlreadyExists);
        }

        var targetFile = existingFile == null
            ? await fileSystem.Storage.CreateInternalFileId(driveId)
            : new InternalDriveFileId(driveId, existingFile.FileId);

        if (existingFile == null)
        {
            // write new file
        }


        // TODO: handle decryption / re-encryption

        // foreach (var payload in header.FileMetadata.Payloads)
        // {
        //     var (encryptedPayloadKeyHeader, payloadIsEncrypted, payloadStream) =
        //         await service.GetPayloadByGlobalTransitIdAsync(remoteIdentity, file, payload.Key, null, fst, odinContext);
        //
        //     if (payloadIsEncrypted)
        //     {
        //         var keyHeader = encryptedPayloadKeyHeader.DecryptAesToKeyHeader()
        //     }
        //
        //     foreach (var thumbnailDescriptor in payload.Thumbnails)
        //     {
        //         var (encryptedThumbnailKeyHeader, thumbnailIsEncrypted, decryptedContentType, lastModified, thumbnailStream) =
        //             await service.GetThumbnailByGlobalTransitIdAsync(remoteIdentity,
        //                 file,
        //                 payload.Key,
        //                 thumbnailDescriptor.PixelWidth,
        //                 thumbnailDescriptor.PixelHeight,
        //                 false,
        //                 fst,
        //                 odinContext);
        //
        //
        //         // handle decryption
        //     }
        // }

        return new FileIdentifier()
        {
            FileId = targetFile.FileId,
            TargetDrive = targetDrive
        };
    }
}