using System;
using Odin.Services.Util;

namespace Odin.Services.Drives.DriveCore.Query;

public class FileQueryParamsV1 : FileQueryParams
{
    public TargetDrive TargetDrive { get; set; }

    public Guid DriveId => TargetDrive?.Alias ?? Guid.Empty;

    public void AssertIsValid()
    {
        OdinValidationUtils.AssertNotEmptyGuid(DriveId, "driveId");
    }

    /// <summary>
    /// Creates a new instance matching the list of filetypes specified
    /// </summary>
    /// <returns></returns>
    public static FileQueryParamsV1 FromFileType(TargetDrive drive, params int[] fileType)
    {
        return new FileQueryParamsV1()
        {
            TargetDrive = drive,
            FileType = fileType
        };
    }
}