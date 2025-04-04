using System;
using System.Collections.Generic;
using Odin.Core.Time;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Util;

namespace Odin.Services.Drives.DriveCore.Query;

public class FileQueryParams
{
    public TargetDrive TargetDrive { get; set; }
    public IEnumerable<int> FileType { get; set; } = null;
  
    public IEnumerable<FileState> FileState { get; set; } = null;

    public IEnumerable<int> DataType { get; set; } = null;

    public IEnumerable<int> ArchivalStatus { get; set; } = null;

    public IEnumerable<string> Sender { get; set; } = null;

    public IEnumerable<Guid> GroupId { get; set; } = null;

    public UnixTimeUtcRange UserDate { get; set; } = null;

    public IEnumerable<Guid> ClientUniqueIdAtLeastOne { get; set; } = null;
    public IEnumerable<Guid> TagsMatchAtLeastOne { get; set; } = null;

    public IEnumerable<Guid> TagsMatchAll { get; set; } = null;
    
    public IEnumerable<Guid> LocalTagsMatchAtLeastOne { get; set; } = null;

    public IEnumerable<Guid> LocalTagsMatchAll { get; set; } = null;
    
    public IEnumerable<Guid> GlobalTransitId { get; set; }

    public void AssertIsValid()
    {
        OdinValidationUtils.AssertIsValidTargetDriveValue(TargetDrive);
    }

    /// <summary>
    /// Creates a new instance matching the list of filetypes specified
    /// </summary>
    /// <returns></returns>
    public static FileQueryParams FromFileType(TargetDrive drive, params int[] fileType)
    {
        return new FileQueryParams()
        {
            TargetDrive = drive,
            FileType = fileType
        };
    }
}

