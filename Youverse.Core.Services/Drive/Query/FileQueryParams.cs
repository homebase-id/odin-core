using System;
using System.Collections.Generic;
using Dawn;

namespace Youverse.Core.Services.Drive.Query;

public class FileQueryParams
{
    public TargetDrive TargetDrive { get; set; }
    public IEnumerable<int> FileType { get; set; } = null;
    public IEnumerable<int> DataType { get; set; } = null;
    public IEnumerable<byte[]> Sender { get; set; } = null;
    
    public IEnumerable<byte[]> GroupId { get; set; } = null;

    public TimeRange UserDate { get; set; } = null;
    
    public IEnumerable<Guid> TagsMatchAtLeastOne { get; set; } = null;

    public IEnumerable<Guid> TagsMatchAll { get; set; } = null;

    public void AssertIsValid()
    {
        Guard.Argument(TargetDrive, nameof(TargetDrive)).NotNull().Require(td => td.IsValid());
    }
}