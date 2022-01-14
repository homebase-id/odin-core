using System;
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
        Task<ClientFileHeader> GetClientEncryptedFileHeader(DriveFileId file);
        
        /// <summary>
        /// Converts a transfer key header to a long term key header and stores it for the specified file.
        /// </summary>
        Task WriteTransferKeyHeader(DriveFileId file, RsaEncryptedRecipientTransferKeyHeader header);

        /// <summary>
        /// Gets a public key used in the transit protocol.
        /// </summary>
        /// <param name="appid">The appid for the key</param>
        /// <param name="crc">If not null a key for the given crc is returned, otherwise the latest key is returned</param>
        /// <returns></returns>
        Task<TransitPublicKey> GetTransitPublicKey(Guid appid, uint? crc = null);
    }
}