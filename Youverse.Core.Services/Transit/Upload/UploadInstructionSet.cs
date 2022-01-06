using Youverse.Core.Services.Drive.Storage;

namespace Youverse.Core.Services.Transit.Upload
{
    /// <summary>
    /// Specifies how an upload should be handled
    /// </summary>
    public class UploadInstructionSet
    {
        /// <summary>
        /// The transfer initialization vector used to encrypt the KeyHeader 
        /// </summary>
        public byte[] TransferIv { get; set; }
        
        public StorageOptions StorageOptions { get; set; }
        
        public TransitOptions TransitOptions { get; set; }

    }
}