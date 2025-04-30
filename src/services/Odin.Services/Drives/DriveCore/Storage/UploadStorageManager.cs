using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;

namespace Odin.Services.Drives.DriveCore.Storage
{
    /// <summary>
    /// Temporary storage for a given driven.  Used to stage incoming file parts from uploads and transfers.
    /// </summary>
    public class UploadStorageManager(
        DriveFileReaderWriter driveFileReaderWriter,
        DriveManager driveManager,
        ILogger<UploadStorageManager> logger)
    {
        public async Task<bool> TempFileExists(TempFile tempFile, string extension)
        {
            string path = await GetTempFilenameAndPathInternal(tempFile, extension);
            return driveFileReaderWriter.FileExists(path);
        }
        
        /// <summary>
        /// Gets a stream of data for the specified file
        /// </summary>
        public async Task<byte[]> GetAllFileBytes(TempFile tempFile, string extension)
        {
            string path = await GetTempFilenameAndPathInternal(tempFile, extension);

            logger.LogDebug("Getting temp file bytes for [{path}]", path);
            var bytes = await driveFileReaderWriter.GetAllFileBytesAsync(path);
            logger.LogDebug("Got {count} bytes from {path}", bytes.Length, path);
            return bytes;
        }

        /// <summary>
        /// Writes a stream for a given file and part to the configured provider.
        /// </summary>
        public async Task<uint> WriteStream(TempFile tempFile, string extension, Stream stream)
        {
            string path = await GetTempFilenameAndPathInternal(tempFile, extension, true);
            logger.LogDebug("Writing temp file: {filePath}", path);
            var bytesWritten = await driveFileReaderWriter.WriteStreamAsync(path, stream);
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
        /// Deletes all files matching <param name="tempFile"></param> regardless of extension
        /// </summary>
        public async Task EnsureDeleted(TempFile tempFile)
        {
            var drive = await driveManager.GetDriveAsync(tempFile.File.DriveId);

            var dir = GetFileDirectory(drive, tempFile);
            var pattern = TenantPathManager.GetFilename(tempFile.File.FileId, "*");

            logger.LogDebug("Delete temp files in dir: {filePath} using searchPattern: {pattern}", dir, pattern);
            driveFileReaderWriter.DeleteFilesInDirectory(dir, pattern);
        }

        /// <summary>
        /// Gets the physical path of the specified file
        /// </summary>
        public async Task<string> GetPath(TempFile tempFile, string extension)
        {
            string path = await GetTempFilenameAndPathInternal(tempFile, extension);
            return path;
        }

        private string GetFileDirectory(StorageDrive drive, TempFile tempFile, bool ensureExists = false)
        {
            //07e5070f-173b-473b-ff03-ffec2aa1b7b8
            //The positions in the time guid are hex values as follows
            //from new DateTimeOffset(2021, 7, 21, 23, 59, 59, TimeSpan.Zero);
            //07e5=year,07=month,0f=day,17=hour,3b=minute

            var parts = tempFile.File.FileId.ToString().Split("-");
            var yearMonthDay = parts[0];
            var year = yearMonthDay.Substring(0, 4);
            var month = yearMonthDay.Substring(4, 2);
            var day = yearMonthDay.Substring(6, 2);
            var hourMinute = parts[1];
            var hour = hourMinute[..2];

            var r = Path.Combine(year, month, day, hour);
            var s = TenantPathManager.GetPayloadDirectoryFromGuid(tempFile.File.FileId);

            if (r != s)
            {
                logger.LogError($"GetFileDirectory mismatch {r} vs {s}");
                Debug.Assert(s == r);
            }

            string path = drive.GetTempStoragePath(tempFile.StorageType);

            // need tenantPathManager injection
            // var t = tenantPathManager.GetDriveTempStoragePath(drive.Id, tempFile.StorageType);

            string dir = Path.Combine(path, year, month, day, hour);

            if (ensureExists)
            {
                Directory.CreateDirectory(dir);
            }

            return dir;
        }

        private async Task<string> GetTempFilenameAndPathInternal(TempFile tempFile, string extension, bool ensureExists = false)
        {
            var drive = await driveManager.GetDriveAsync(tempFile.File.DriveId);
            var fileId = tempFile.File.FileId;

            string dir = GetFileDirectory(drive, tempFile, ensureExists);
            var r =  Path.Combine(dir, TenantPathManager.GetFilename(fileId, extension));

            return r;
        }
        
    }
}