using System.IO;
using System.Threading.Tasks;
using Odin.Services.Base;

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
    /// A payloadHash part: the values of the payload's deferred hash (#1935)
    /// </summary>
    Task AddPayloadHash(string key, Stream data);
}
