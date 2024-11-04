using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Services.Drives.FileSystem.Base.Update
{
    public class UpdateFileDescriptor
    {
        /// <summary>
        /// The new IV used on the key header
        /// </summary>
        public byte[] KeyHeaderIv { get; init; }

        public UploadFileMetadata FileMetadata { get; init; }
    }
}