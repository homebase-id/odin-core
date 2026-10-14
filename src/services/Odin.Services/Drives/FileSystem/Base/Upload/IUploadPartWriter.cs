using System.IO;
using System.Threading.Tasks;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Drives.FileSystem.Base.Upload;

/// <summary>
/// The multipart parts that follow an upload's instructions, common to the upload, update and add-payload writers,
/// so one controller loop serves every upload route
/// </summary>
public interface IUploadPartWriter
{
    Task AddPayload(string key, string contentTypeFromMultipartSection, Stream data, IOdinContext odinContext);

    Task AddThumbnail(string thumbnailUploadKey, string contentTypeFromMultipartSection, Stream data, IOdinContext odinContext);

    /// <summary>
    /// The payload hashes of this upload, verified as each payload is written or, when deferred (#1935), once its
    /// payloadHash part arrives
    /// </summary>
    DeferredPayloadHashes PayloadHashes { get; }
}
