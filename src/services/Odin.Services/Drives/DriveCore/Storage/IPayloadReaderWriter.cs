using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Odin.Services.Drives.DriveCore.Storage;

#nullable enable

public interface IPayloadReaderWriter
{
    Task WriteFileAsync(string filePath, byte[] bytes, CancellationToken cancellationToken = default);
    Task DeleteFileAsync(string filePath, CancellationToken cancellationToken = default);
    Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default);
    Task MoveFileAsync(string srcFilePath, string dstFilePath, CancellationToken cancellationToken = default);

    void MoveFileXYZ(string sourceFilePath, string destinationFilePath); // SEB:TODO
    string[] GetFilesInDirectoryXYZ(string dir, string searchPattern = "*"); // SEB:TODO
    void DeleteFilesInDirectoryXYZ(string dir, string searchPattern);  // SEB:TODO
    bool DirectoryExistsXYZ(string dir); // SEB:TODO
    Stream OpenStreamForReadingXYZ(string filePath); // SEB:TODO
    void CopyPayloadFileXYZ(string sourcePath, string targetPath); // SEB:TODO
    void CreateDirectoryXYZ(string dir);
}

//


