using System;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Exceptions;

namespace Odin.Services.Drives.DriveCore.Storage;

#nullable enable

public interface IPayloadReaderWriter
{
    Task WriteFileAsync(string filePath, byte[] bytes, CancellationToken cancellationToken = default);
    Task DeleteFileAsync(string filePath, CancellationToken cancellationToken = default);
    Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default);
    Task MoveFileAsync(string srcFilePath, string dstFilePath, CancellationToken cancellationToken = default);
    Task CreateDirectoryAsync(string dir, CancellationToken cancellationToken = default);
    Task CopyPayloadFileAsync(string sourcePath, string targetPath, CancellationToken cancellationToken = default);
    Task<byte[]> GetFileBytesAsync(string filePath, CancellationToken cancellationToken = default);
    Task<byte[]> GetFileBytesAsync(string filePath, long start, long length, CancellationToken cancellationToken = default);
}

//

public class PayloadReaderWriterException : OdinSystemException
{
    public PayloadReaderWriterException(string message) : base(message)
    {
    }

    public PayloadReaderWriterException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
