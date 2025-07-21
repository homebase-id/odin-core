using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Peer.Outgoing.Drive.Query;

namespace Odin.Services.Peer.DataCopy;

public class PeerDataCopyService(OutgoingPeerDriveQueryService service, FileSystemResolver fileSystemResolver)
{
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

        var fileSystem = fileSystemResolver.ResolveFileSystem(fst);
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

        foreach (var payload in header.FileMetadata.Payloads)
        {
            foreach (var thumbnailDescriptor in payload.Thumbnails)
            {
                var (encryptedPayloadKeyHeader, payloadIsEncrypted, payloadStream) =
                    await service.GetPayloadByGlobalTransitIdAsync(remoteIdentity, file, payload.Key, null, fst, odinContext);

                var (encryptedThumbnailKeyHeader, thumbnailIsEncrypted, decryptedContentType, lastModified, thumbnailStream) =
                    await service.GetThumbnailByGlobalTransitIdAsync(remoteIdentity,
                        file,
                        payload.Key,
                        thumbnailDescriptor.PixelWidth,
                        thumbnailDescriptor.PixelHeight,
                        false,
                        fst,
                        odinContext);


                // handle decryption
            }
        }

        return new FileIdentifier()
        {
            FileId = targetFile.FileId,
            TargetDrive = targetDrive
        };
    }
}