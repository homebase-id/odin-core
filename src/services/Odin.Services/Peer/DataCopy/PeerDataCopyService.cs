using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Peer.Outgoing.Drive.Query;

namespace Odin.Services.Peer.DataCopy;

public class PeerDataCopyService(OutgoingPeerDriveQueryService service)
{
    public async Task CopyFile(OdinId remoteIdentity, FileIdentifier source, TargetDrive targetDrive,
        FileSystemType fst,
        IOdinContext odinContext)
    {
        var file = source.ToGlobalTransitIdFileIdentifier();
        odinContext.PermissionsContext.AssertCanWriteToDrive(targetDrive.Alias);
        var header = await service.GetFileHeaderByGlobalTransitIdAsync(remoteIdentity,
            file, fst, odinContext);

        if (null == header)
        {
            throw new OdinClientException("Remote file does not exist", OdinClientErrorCode.InvalidFile);
        }

        // TODO: handle decryption

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
    }
}