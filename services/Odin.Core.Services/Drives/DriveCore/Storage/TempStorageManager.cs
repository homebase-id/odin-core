using System;
using System.IO;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;

namespace Odin.Core.Services.Drives.DriveCore.Storage
{
    /// <summary>
    /// Temporary storage for a given driven.  Used to stage incoming file parts from uploads and transfers.
    /// </summary>
    public class TempStorageManager
    {
        private readonly ILogger<TempStorageManager> _logger;

        private readonly StorageDrive _drive;
        private const int WriteChunkSize = 1024;

        public TempStorageManager(StorageDrive drive, ILogger<TempStorageManager> logger)
        {
            Guard.Argument(drive, nameof(drive)).NotNull();
            // Guard.Argument(drive, nameof(drive)).Require(sd => Directory.Exists(sd.LongTermDataRootPath), sd => $"No directory for drive storage at {sd.LongTermDataRootPath}");
            // Guard.Argument(drive, nameof(drive)).Require(sd => Directory.Exists(sd.TempDataRootPath), sd => $"No directory for drive storage at {sd.TempDataRootPath}");

            drive.EnsureDirectories();

            _logger = logger;
            _drive = drive;
        }

        /// <summary>
        /// The drive managed by this instance
        /// </summary>
        public StorageDrive Drive { get; }

        /// <summary>
        /// Gets a stream of data for the specified file
        /// </summary>
        public Task<Stream> GetStream(Guid fileId, string extension)
        {
            string path = GetFilenameAndPath(fileId, extension);
            var fileStream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            return Task.FromResult((Stream)fileStream);
        }

        /// <summary>
        /// Writes a stream for a given file and part to the configured provider.
        /// </summary>
        public Task<uint> WriteStream(Guid fileId, string extension, Stream stream)
        {
            //TODO: this is probably highly inefficient and probably need to revisit 
            string filePath = GetFilenameAndPath(fileId, extension, true);
            string tempFilePath = GetTempFilePath(fileId, extension);

            uint bytesWritten;
            try
            {
                //Process: if there's a file, we write to a temp file then rename.
                if (File.Exists(filePath))
                {
                    bytesWritten = WriteStream(stream, tempFilePath);
                    lock (filePath)
                    {
                        // File.WriteAllBytes(filePath, stream.ToByteArray());
                        //TODO: need to know if this replace method is faster than renaming files
                        File.Replace(tempFilePath, filePath, null, true);
                    }
                }
                else
                {
                    bytesWritten = WriteStream(stream, filePath);
                }
            }
            finally
            {
                //TODO: should clean up the temp file in case of failure?
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }

            return Task.FromResult(bytesWritten);
        }

        /// <summary>
        /// Checks if the file exists.  Returns true if all parts exist, otherwise false
        /// </summary>
        public bool FileExists(Guid fileId, string extension)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Deletes the file matching <param name="fileId"></param> and extension.
        /// </summary>
        public Task EnsureDeleted(Guid fileId, string extension)
        {
            string filePath = GetFilenameAndPath(fileId, extension);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Deletes all files matching <param name="fileId"></param> regardless of extension
        /// </summary>
        /// <param name="fileId"></param>
        public Task EnsureDeleted(Guid fileId)
        {
            var dir = new DirectoryInfo(GetFileDirectory(fileId));

            if (dir.Exists)
            {
                foreach (var file in dir.EnumerateFiles(GetFilename(fileId, "*")))
                {
                    file.Delete();
                }
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Gets the physical path of the specified file
        /// </summary>
        public Task<string> GetPath(Guid fileId, string extension)
        {
            string filePath = GetFilenameAndPath(fileId, extension);
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
            string file = fileId.ToString();
            return string.IsNullOrEmpty(extension) ? file : $"{file}.{extension.ToLower()}";
        }

        private string GetFilenameAndPath(Guid fileId, string extension, bool ensureExists = false)
        {
            string dir = GetFileDirectory(fileId, ensureExists);
            return Path.Combine(dir, GetFilename(fileId, extension));
        }

        private string GetTempFilePath(Guid fileId, string extension, bool ensureExists = false)
        {
            string dir = GetFileDirectory(fileId, ensureExists);
            string filename = $"{Guid.NewGuid()}-{extension}.tmp";
            return Path.Combine(dir, filename);
        }

        private uint WriteStream(Stream stream, string filePath)
        {
            var buffer = new byte[WriteChunkSize];

            using (var output = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                var bytesRead = 0;
                do
                {
                    // stream.ReadAsync(buffer, 0, buffer.Length).GetAwaiter().GetResult();
                    bytesRead = stream.Read(buffer, 0, buffer.Length);
                    output.Write(buffer, 0, bytesRead);
                } while (bytesRead > 0);

                var bytesWritten = output.Length;
                output.Close();
                return (uint)bytesWritten;
            }
        }
    }
}