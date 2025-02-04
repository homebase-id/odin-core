using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;

namespace Odin.Services.Drives.FileSystem.Base.Update
{
    public class UpdateFileDescriptor
    {
        // /// <summary>
        // /// The new IV used on the key header
        // /// </summary>
        // public byte[] KeyHeaderIv { get; init; }
        
        /// <summary>
        ///  
        /// </summary>
        public KeyHeader KeyHeader { get; init; }

        public UploadFileMetadata FileMetadata { get; init; }
    }
}