using System;
using System.Threading.Tasks;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Apps
{
    /// <summary>
    /// App specific functions like retrieving file headers
    /// </summary>
    public interface IAppService
    {
        /// <summary>
        /// Gets the file header information encrypted using the app's shared secret for the requesting client
        /// </summary>
        Task<ClientFileHeader> GetClientEncryptedFileHeader(InternalDriveFileId file);
    }
}