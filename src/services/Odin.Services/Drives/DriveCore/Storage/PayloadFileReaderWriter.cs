using System.IO;
using Microsoft.Extensions.Logging;
using Odin.Services.Base;

namespace Odin.Services.Drives.DriveCore.Storage;

#nullable enable

public class PayloadFileReaderWriter(
    ILogger<PayloadFileReaderWriter> logger,
    TenantContext tenantContext,
    FileReaderWriter fileReaderWriter
) : AbstractPayloadReaderWriter
{
    public override void DeleteFile(string fileName)
    {
        ValidateFilename(fileName);
        fileReaderWriter.DeleteFile(fileName);
    }

    public override void DeleteFileXYZ(string path)
    {
        throw new System.NotImplementedException();
    }

    public override bool FileExistsXYZ(string filePath)
    {
        throw new System.NotImplementedException();
    }

    public override void MoveFileXYZ(string sourceFilePath, string destinationFilePath)
    {
        throw new System.NotImplementedException();
    }

    public override string[] GetFilesInDirectoryXYZ(string dir, string searchPattern = "*")
    {
        throw new System.NotImplementedException();
    }

    public override void DeleteFilesInDirectoryXYZ(string dir, string searchPattern)
    {
        throw new System.NotImplementedException();
    }

    public override bool DirectoryExistsXYZ(string dir)
    {
        throw new System.NotImplementedException();
    }

    public override Stream OpenStreamForReadingXYZ(string filePath)
    {
        throw new System.NotImplementedException();
    }

    public override void CopyPayloadFileXYZ(string sourcePath, string targetPath)
    {
        throw new System.NotImplementedException();
    }

    public override void CreateDirectoryXYZ(string dir)
    {
        throw new System.NotImplementedException();
    }
}