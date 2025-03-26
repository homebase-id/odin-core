using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Services.Drives.FileSystem.Base;

namespace Odin.Services.Drives.DriveCore.Storage
{
    /// <summary>
    /// Temporary storage for a given driven.  Used to stage incoming file parts from uploads and transfers.
    /// </summary>
    public class TempStorageManager(DriveFileReaderWriter driveFileReaderWriter, ILogger<TempStorageManager> logger)
    {
        // public StorageDrive Drive { get; }

        /// <summary>
        /// Gets a stream of data for the specified file
        /// </summary>
        public async Task<byte[]> GetAllFileBytes(StorageDrive drive, Guid fileId, string extension)
        {
            string path = GetTempFilenameAndPath(drive, fileId, extension);
            logger.LogDebug("Getting temp file bytes for [{path}]", path);
            var bytes = await driveFileReaderWriter.GetAllFileBytes(path);
            logger.LogDebug("Got {count} bytes from {path}", bytes.Length, path);
            return bytes;
        }

        /// <summary>
        /// Writes a stream for a given file and part to the configured provider.
        /// </summary>
        public async Task<uint> WriteStream(StorageDrive drive, Guid fileId, string extension, Stream stream)
        {
            string filePath = GetTempFilenameAndPath(drive, fileId, extension, true);
            logger.LogDebug("Writing temp file: {filePath}", filePath);
            var bytesWritten = await driveFileReaderWriter.WriteStream(filePath, stream);
            if (bytesWritten == 0)
            {
                // Sanity #1
                logger.LogDebug("I didn't write anything to {filePath}", filePath);
            }
            else if (!File.Exists(filePath))
            {
                // Sanity #2
                logger.LogError("I wrote {count} bytes, but file is not there {filePath}", bytesWritten, filePath);
            }
            logger.LogDebug("Wrote {count} bytes to {filePath}", bytesWritten, filePath);

            return bytesWritten;
        }

        /// <summary>
        /// Deletes all files matching <param name="fileId"></param> regardless of extension
        /// </summary>
        /// <param name="drive"></param>
        /// <param name="fileId"></param>
        public async Task EnsureDeleted(StorageDrive drive, Guid fileId)
        {
            // var dir = new DirectoryInfo(GetFileDirectory(fileId));
            var dir = GetFileDirectory(drive, fileId);
            // logger.LogDebug("Delete temp files in dir: {filePath}", dir);
            // await driveFileReaderWriter.DeleteFilesInDirectoryAsync(dir, searchPattern: GetFilename(fileId, "*"));
            
            await Task.CompletedTask;
            logger.LogDebug("no-op: delete on temp files called yet we've removed this. path {filePath}", dir);
        }

        /// <summary>
        /// Gets the physical path of the specified file
        /// </summary>
        public Task<string> GetPath(StorageDrive drive, Guid fileId, string extension)
        {
            string filePath = GetTempFilenameAndPath(drive, fileId, extension);
            return Task.FromResult(filePath);
        }

        private string GetFileDirectory(StorageDrive drive, Guid fileId, bool ensureExists = false)
        {
            string path = drive.GetTempStoragePath();

            //07e5070f-173b-473b-ff03-ffec2aa1b7b8
            //The positions in the time guid are hex values as follows
            //from new DateTimeOffset(2021, 7, 21, 23, 59, 59, TimeSpan.Zero);
            //07e5=year,07=month,0f=day,17=hour,3b=minute

            var parts = fileId.ToString().Split("-");
            var yearMonthDay = parts[0];
            var year = yearMonthDay.Substring(0, 4);
            var month = yearMonthDay.Substring(4, 2);
            var day = yearMonthDay.Substring(6, 2);
            var hourMinute = parts[1];
            var hour = hourMinute[..2];

            string dir = Path.Combine(path, year, month, day, hour);

            if (ensureExists)
            {
                Directory.CreateDirectory(dir);
            }

            return dir;
        }

        private string GetFilename(Guid fileId, string extension)
        {
            string file = DriveFileUtility.GetFileIdForStorage(fileId);
            return string.IsNullOrEmpty(extension) ? file : $"{file}.{extension.ToLower()}";
        }

        private string GetTempFilenameAndPath(StorageDrive drive, Guid fileId, string extension, bool ensureExists = false)
        {
            string dir = GetFileDirectory(drive, fileId, ensureExists);
            return Path.Combine(dir, GetFilename(fileId, extension));
        }
    }
}