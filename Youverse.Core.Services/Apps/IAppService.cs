using System.IO;
using System.Threading.Tasks;
using Youverse.Core.Services.Drive;

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
    }
}