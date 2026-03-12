using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Services.Drives.FileSystem.Base;

namespace Odin.Services.Drives.DriveCore.Storage
{
    public class FileHandlerShared(
        FileReaderWriter fileReaderWriter,
        ILogger<FileHandlerShared> logger)
    {
        public bool FileExists(string path)
        {
            return fileReaderWriter.FileExists(path);
        }

        public async Task<byte[]> GetAllFileBytesAsync(string path)
        {
            logger.LogDebug("Getting temp file bytes for [{path}]", path);
            var bytes = await fileReaderWriter.GetAllFileBytesAsync(path);
            logger.LogDebug("Got {count} bytes from {path}", bytes.Length, path);
            return bytes;
        }

        public async Task<uint> WriteStreamAsync(string path, Stream stream)
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

        public void DeleteFiles(IEnumerable<string> paths)
        {
            fileReaderWriter.DeleteFiles(paths);
        }

        public string BuildTempFilePath(string dir, Guid fileId, string ext)
        {
            return Path.Combine(dir, TenantPathManager.GetFilename(fileId, ext));
        }

        public void EnsureDirectoryExists(string path)
        {
            Directory.CreateDirectory(path);
        }
    }
}
