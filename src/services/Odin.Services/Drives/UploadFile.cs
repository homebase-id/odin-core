#nullable enable
namespace Odin.Services.Drives;

public class UploadFile
{
    public InternalDriveFileId FileId { get; }

    public UploadFile(InternalDriveFileId fileId)
    {
        fileId.Validate();
        FileId = fileId;
    }

    public override string ToString()
    {
        return $"UploadFile: {FileId}";
    }
}
