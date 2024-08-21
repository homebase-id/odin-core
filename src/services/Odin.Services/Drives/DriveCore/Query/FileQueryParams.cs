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

    /// <summary>
    /// List of byte[] where the content is a lower-cased UTF8 encoded byte array of the identity.
    /// </summary>
    public IEnumerable<byte[]> Sender { get; set; } = null;

    public IEnumerable<Guid> GroupId { get; set; } = null;

    public UnixTimeUtcRange UserDate { get; set; } = null;

    public IEnumerable<Guid> ClientUniqueIdAtLeastOne { get; set; } = null;
    public IEnumerable<Guid> TagsMatchAtLeastOne { get; set; } = null;

    public IEnumerable<Guid> TagsMatchAll { get; set; } = null;

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