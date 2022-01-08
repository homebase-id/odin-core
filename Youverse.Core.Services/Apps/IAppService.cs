using System.IO;
using System.Threading.Tasks;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Apps
{
    /// <summary>
    /// App specific functions like retrieving file headers
    /// </summary>
    public interface IAppService
    {
        /// <summary>
        /// Gets the file header information encrypted using the app's shared secret for the requesting device
        /// </summary>
        Task<ClientFileHeader> GetDeviceEncryptedFileHeader(DriveFileId file);
        
        /// <summary>
        /// Converts a transfer key header to a long term key header and stores it for the specified file.
        /// </summary>
        Task<EncryptedKeyHeader> WriteTransferKeyHeader(DriveFileId file, EncryptedKeyHeader transferEncryptedKeyHeader, StorageDisposition storageDisposition);

    }
}