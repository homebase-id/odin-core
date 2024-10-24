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
    public class TempStorageManager
    {
        private readonly DriveFileReaderWriter _driveFileReaderWriter;
        private readonly ILogger<TempStorageManager> _logger;

        private readonly StorageDrive _drive;

        public TempStorageManager(StorageDrive drive, DriveFileReaderWriter driveFileReaderWriter, ILogger<TempStorageManager> logger)
        {
            drive.EnsureDirectories();

            _driveFileReaderWriter = driveFileReaderWriter;
            _logger = logger;
            _drive = drive;
        }

        // public StorageDrive Drive { get; }

        /// <summary>
        /// Gets a stream of data for the specified file
        /// </summary>
        public async Task<byte[]> GetAllFileBytes(Guid fileId, string extension)
        {
            string path = GetTempFilenameAndPath(fileId, extension);
            _logger.LogDebug("Getting temp file bytes for [{path}]", path);
            var bytes = await _driveFileReaderWriter.GetAllFileBytes(path);
            return bytes;
        }

        /// <summary>
        /// Writes a stream for a given file and part to the configured provider.
        /// </summary>
        public async Task<uint> WriteStream(Guid fileId, string extension, Stream stream)
        {
            string filePath = GetTempFilenameAndPath(fileId, extension, true);
            uint bytesWritten = await _driveFileReaderWriter.WriteStream(filePath, stream);
            return bytesWritten;
        }

        /// <summary>
        /// Deletes the file matching <param name="fileId"></param> and extension.
        /// </summary>
        public async Task EnsureDeleted(Guid fileId, string extension)
        {
            string filePath = GetTempFilenameAndPath(fileId, extension);
            await _driveFileReaderWriter.DeleteFileAsync(filePath);
        }

        /// <summary>
        /// Deletes all files matching <param name="fileId"></param> regardless of extension
        /// </summary>
        /// <param name="fileId"></param>
        public async Task EnsureDeleted(Guid fileId)
        {
            // var dir = new DirectoryInfo(GetFileDirectory(fileId));
            var dir = GetFileDirectory(fileId);
            await _driveFileReaderWriter.DeleteFilesInDirectoryAsync(dir, searchPattern: GetFilename(fileId, "*"));
        }

        /// <summary>
        /// Gets the physical path of the specified file
        /// </summary>
        public Task<string> GetPath(Guid fileId, string extension)
        {
            string filePath = GetTempFilenameAndPath(fileId, extension);
            return Task.FromResult(filePath);
        }

        private string GetFileDirectory(Guid fileId, bool ensureExists = false)
        {
            string path = _drive.GetTempStoragePath();

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

        private string GetTempFilenameAndPath(Guid fileId, string extension, bool ensureExists = false)
        {
            string dir = GetFileDirectory(fileId, ensureExists);
            return Path.Combine(dir, GetFilename(fileId, extension));
        }
    }
}