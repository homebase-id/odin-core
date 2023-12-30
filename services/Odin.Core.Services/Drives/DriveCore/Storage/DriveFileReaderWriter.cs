using System.IO;
using Odin.Core.Services.Configuration;
using Odin.Core.Util;

namespace Odin.Core.Services.Drives.DriveCore.Storage;

/// <summary>
/// Handles read/write access to drive files to ensure correct
/// locking as well as apply system config for how files are written.
/// </summary>
public sealed class DriveFileReaderWriter
{
    private readonly ConcurrentFileManager _concurrentFileManager;
    private readonly OdinConfiguration _configuration;

    public DriveFileReaderWriter(OdinConfiguration configuration)
    {
        _concurrentFileManager = new ConcurrentFileManager();
        _configuration = configuration;
    }

    public void WriteString(string filePath, string data)
    {
        _concurrentFileManager.WriteFile(filePath, path => WriteStringInternal(path, data));
    }

    public void WriteAllBytes(string filePath, byte[] bytes)
    {
        _concurrentFileManager.WriteFile(filePath, path => WriteAllBytesInternal(path, bytes));
    }

    public uint WriteStream(string filePath, Stream stream)
    {
        uint bytesWritten = 0;
        _concurrentFileManager.WriteFile(filePath,
            path => WriteStreamInternal(path, stream, _configuration.Host.FileWriteChunkSizeInBytes, out bytesWritten));
        return bytesWritten;
    }

    private static void WriteStreamInternal(string filePath, Stream stream, int chunkSize, out uint bytesWritten)
    {
        var buffer = new byte[chunkSize];

        using (var output = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var bytesRead = 0;
            do
            {
                bytesRead = stream.Read(buffer, 0, buffer.Length);
                output.Write(buffer, 0, bytesRead);
            } while (bytesRead > 0);

            bytesWritten = (uint)output.Length;
            output.Close();
        }
    }

    private static void WriteStringInternal(string filePath, string data)
    {
        File.WriteAllText(filePath, data);
    }


    private static void WriteAllBytesInternal(string filePath, byte[] bytes)
    {
        File.WriteAllBytes(filePath, bytes);
    }

    public byte[] GetAllFileBytes(string filePath)
    {
        byte[] bytes = null;
        _concurrentFileManager.ReadFile(filePath, path => bytes = File.ReadAllBytes(path));

        //TODO: add server warning configuration when too my bytes are read
        // if (bytes.Length > _configuration.Host.BytesWarningSize)
        // {
        //     Log.Warning("...");
        // }

        return bytes;
    }

    public void MoveFile(string sourceFilePath, string destinationFilePath)
    {
        _concurrentFileManager.MoveFile(sourceFilePath, destinationFilePath, (s, d) =>
            // File.Replace(s, d, null) //Replace requires the destination file to exist
            File.Move(s, d, true)
        );
    }

    /// <summary>
    /// Opens a filestream.  You must remember to close it.
    /// </summary>
    public Stream OpenStreamForReading(string filePath, FileShare fileShare = FileShare.ReadWrite)
    {
        Stream fileStream = null;
        _concurrentFileManager.ReadFile(filePath, path =>
        {
            // fileStream = File.Open(path, FileMode.Open, FileAccess.Read, fileShare);
            fileStream = new OdinFilestream(path, FileMode.Open, FileAccess.Read, fileShare);
        });
        
        return fileStream;
    }
}