using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Odin.Services.Drives.DriveCore.Storage;

#nullable enable

public interface IPayloadReaderWriter
{
    void DeleteFile(string fileName);

    void DeleteFileXYZ(string path); // SEB:TODO
    bool FileExistsXYZ(string filePath); // SEB:TODO
    void MoveFileXYZ(string sourceFilePath, string destinationFilePath); // SEB:TODO
    string[] GetFilesInDirectoryXYZ(string dir, string searchPattern = "*"); // SEB:TODO
    void DeleteFilesInDirectoryXYZ(string dir, string searchPattern);  // SEB:TODO
    bool DirectoryExistsXYZ(string dir); // SEB:TODO
    Stream OpenStreamForReadingXYZ(string filePath); // SEB:TODO
    void CopyPayloadFileXYZ(string sourcePath, string targetPath); // SEB:TODO
    void CreateDirectoryXYZ(string dir);
}

//

public abstract class AbstractPayloadReaderWriter : IPayloadReaderWriter
{
    public abstract void DeleteFile(string fileName);
    
    public abstract void DeleteFileXYZ(string path);
    public abstract bool FileExistsXYZ(string filePath);
    public abstract void MoveFileXYZ(string sourceFilePath, string destinationFilePath);
    public abstract string[] GetFilesInDirectoryXYZ(string dir, string searchPattern = "*");
    public abstract void DeleteFilesInDirectoryXYZ(string dir, string searchPattern);
    public abstract bool DirectoryExistsXYZ(string dir);
    public abstract Stream OpenStreamForReadingXYZ(string filePath);
    public abstract void CopyPayloadFileXYZ(string sourcePath, string targetPath);
    public abstract void CreateDirectoryXYZ(string dir);

    //

    private static readonly Regex SafeChars = new (@"^[a-zA-Z0-9\-_.]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    //

    public static void ValidateFilename(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("Filename cannot be null or empty.", nameof(fileName));
        }

        if (!SafeChars.IsMatch(fileName))
        {
            throw new ArgumentException("Filename contains invalid characters.", nameof(fileName));
        }
    }
}

