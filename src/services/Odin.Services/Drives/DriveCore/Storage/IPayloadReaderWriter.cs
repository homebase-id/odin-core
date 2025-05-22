using System;
using System.IO;
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
    Task<string[]> GetFilesInDirectoryAsync(string dir, CancellationToken cancellationToken = default);
    Task CreateDirectoryAsync(string dir, CancellationToken cancellationToken = default);

    Stream OpenStreamForReadingXYZ(string filePath); // SEB:TODO
    void CopyPayloadFileXYZ(string sourcePath, string targetPath); // SEB:TODO


    bool DirectoryExistsXYZ(string dir); // SEB:TODO
    string[] GetFilesInDirectoryXYZ(string dir, string searchPattern = "*"); // SEB:TODO
    void DeleteFilesInDirectoryXYZ(string dir, string searchPattern);  // SEB:TODO

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
