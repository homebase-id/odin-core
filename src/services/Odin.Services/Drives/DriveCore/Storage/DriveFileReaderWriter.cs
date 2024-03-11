using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Util;
using Odin.Services.Configuration;

namespace Odin.Services.Drives.DriveCore.Storage;

/// <summary>
/// Handles read/write access to drive files to ensure correct
/// locking as well as apply system config for how files are written.
/// </summary>
public sealed class DriveFileReaderWriter(OdinConfiguration configuration, ConcurrentFileManager concurrentFileManager)
{
    public void WriteString(string filePath, string data)
    {
        concurrentFileManager.WriteFile(filePath, path => File.WriteAllText(path, data));
    }

    public void WriteAllBytes(string filePath, byte[] bytes)
    {
        concurrentFileManager.WriteFile(filePath, path => WriteAllBytesInternal(path, bytes));
    }

    public uint WriteStream(string filePath, Stream stream)
    {
        uint bytesWritten = 0;
        concurrentFileManager.WriteFile(filePath,
            path => WriteStreamInternal(path, stream, configuration.Host.FileWriteChunkSizeInBytes, out bytesWritten));

        if (bytesWritten != stream.Length)
        {
            throw new OdinSystemException($"Failed to write all expected data in stream. Wrote {bytesWritten} but should have been {stream.Length}");
        }

        return bytesWritten;
    }

    public Task<byte[]> GetAllFileBytes(string filePath)
    {
        //Note: i capture the file not found exception to avoid the extra call to File.Exists
        try
        {
            byte[] bytes = null;

            concurrentFileManager.ReadFile(filePath, path => bytes = File.ReadAllBytes(path));
            //TODO: add server warning configuration when too my bytes are read
            // if (bytes.Length > _configuration.Host.BytesWarningSize)
            // {
            //     Log.Warning("...");
            // }

            return Task.FromResult(bytes);
        }
        catch (FileNotFoundException)
        {
            return Task.FromResult<byte[]>(null);
        }
    }

    public void MoveFile(string sourceFilePath, string destinationFilePath)
    {
        concurrentFileManager.MoveFile(sourceFilePath, destinationFilePath, (s, d) =>
            File.Move(s, d, true)
        );
    }

    /// <summary>
    /// Opens a filestream.  You must remember to close it.  Always opens in Read mode.
    /// </summary>
    public Stream OpenStreamForReading(string filePath, FileShare fileShare = FileShare.Read)
    {
        Stream fileStream = concurrentFileManager.ReadStream(filePath); // MS: The CFM opens in ReadOnly mode. 
        return fileStream;
    }

    private static void WriteStreamInternal(string filePath, Stream stream, int chunkSize, out uint bytesWritten)
    {
        var buffer = new byte[chunkSize];

        using (var output = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            int bytesRead;
            do
            {
                bytesRead = stream.Read(buffer, 0, buffer.Length);
                output.Write(buffer, 0, bytesRead);
            } while (bytesRead > 0);

            bytesWritten = (uint)output.Length;
            output.Close();
        }
    }

    private static void WriteAllBytesInternal(string filePath, byte[] bytes)
    {
        File.WriteAllBytes(filePath, bytes);
    }

    public Task DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public Task DeleteFiles(string[] paths)
    {
        foreach (var path in paths)
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public Task DeleteFilesInPath(DirectoryInfo dir, string searchPattern)
    {
        if (dir.Exists)
        {
            foreach (var file in dir.EnumerateFiles(searchPattern))
            {
                file.Delete();
            }
        }

        return Task.CompletedTask;
    }

    public Task<bool> FileExists(string filePath)
    {
        return Task.FromResult(File.Exists(filePath));
    }

    public Task<bool> DirectoryExists(string dir)
    {
        return Task.FromResult(Directory.Exists(dir));
    }

    public async Task DeleteFilesInDirectory(string dir, string searchPattern)
    {
        if (Directory.Exists(dir))
        {
            var thumbnails = Directory.GetFiles(dir, searchPattern);
            await this.DeleteFiles(thumbnails);
        }
    }

    public Task<string[]> GetFilesInDirectory(string dir, string searchPattern = "*")
    {
        return Task.FromResult(Directory.GetFiles(dir!, searchPattern));
    }

    public Task CreateDirectory(string dir)
    {
        Directory.CreateDirectory(dir);
        return Task.CompletedTask;
    }
}