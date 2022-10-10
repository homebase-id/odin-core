using System;
using System.Collections.Generic;
using Dawn;

namespace Youverse.Core.Services.Drive.Query;

public class FileQueryParams
{
    public TargetDrive TargetDrive { get; set; }
    public IEnumerable<int> FileType { get; set; } = null;
    public IEnumerable<int> DataType { get; set; } = null;

    /// <summary>
    /// List of byte[] where the content is a lower-cased UTF8 encoded byte array of the identity.
    /// </summary>
    public IEnumerable<byte[]> Sender { get; set; } = null;

    public IEnumerable<Guid> GroupId { get; set; } = null;

    public TimeRange UserDate { get; set; } = null;

    public IEnumerable<Guid> TagsMatchAtLeastOne { get; set; } = null;

    public IEnumerable<Guid> TagsMatchAll { get; set; } = null;
    
    public IEnumerable<Guid> GlobalTransitId { get; set; }

    public void AssertIsValid()
    {
        Guard.Argument(TargetDrive, nameof(TargetDrive)).NotNull().Require(td => td.IsValid());
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