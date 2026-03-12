using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Services.Base;
using Odin.Services.Drives.FileSystem.Base;

namespace Odin.Services.Drives.DriveCore.Storage
{
    /// <summary>
    /// Shared file handling logic for temporary storage operations.
    /// </summary>
    public class FileHandlerShared(
        FileReaderWriter fileReaderWriter,
        ILogger logger)
    {
        /// <summary>
        /// Checks if a temp file exists at the specified path.
        /// </summary>
        public bool TempFileExists(string path)
        {
            return fileReaderWriter.FileExists(path);
        }

        /// <summary>
        /// Gets all bytes from the file at the specified path.
        /// </summary>
        public async Task<byte[]> GetAllFileBytes(string path)
        {
            logger.LogDebug("Getting temp file bytes for [{path}]", path);
            var bytes = await fileReaderWriter.GetAllFileBytesAsync(path);
            logger.LogDebug("Got {count} bytes from {path}", bytes.Length, path);
            return bytes;
        }

        /// <summary>
        /// Writes a stream to the file at the specified path.
        /// </summary>
        public async Task<uint> WriteStream(string path, Stream stream)
        {
            logger.LogDebug("Writing temp file: {filePath}", path);
            var bytesWritten = await fileReaderWriter.WriteStreamAsync(path, stream);
            if (bytesWritten == 0)
            {
                // Sanity #1
                logger.LogDebug("I didn't write anything to {filePath}", path);
            }
            else if (!File.Exists(path))
            {
                // Sanity #2
                logger.LogError("I wrote {count} bytes, but file is not there {filePath}", bytesWritten, path);
            }

            logger.LogDebug("Wrote {count} bytes to {filePath}", bytesWritten, path);

            return bytesWritten;
        }

        /// <summary>
        /// Gets the full path for a temp file given the directory, temp file info, and extension.
        /// </summary>
        public string GetTempFilenameAndPath(string dir, TempFile tempFile, string extension)
        {
            var fileId = tempFile.File.FileId;
            return Path.Combine(dir, TenantPathManager.GetFilename(fileId, extension));
        }
    }
}