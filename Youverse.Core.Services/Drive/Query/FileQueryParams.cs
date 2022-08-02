using System.Collections.Generic;
using Dawn;

namespace Youverse.Core.Services.Drive.Query;

public class FileQueryParams
{
    public TargetDrive TargetDrive { get; set; }
    public IEnumerable<int> FileType { get; set; } = null;
    public IEnumerable<int> DataType { get; set; } = null;
    public IEnumerable<byte[]> Sender { get; set; } = null;
    
    public IEnumerable<byte[]> ThreadId { get; set; } = null;

    public TimeRange UserDate { get; set; } = null;
    
    public IEnumerable<byte[]> TagsMatchAtLeastOne { get; set; } = null;

    public IEnumerable<byte[]> TagsMatchAll { get; set; } = null;

    public void AssertIsValid()
    {
        Guard.Argument(TargetDrive, nameof(TargetDrive)).NotNull().Require(td => td.IsValid());
    }
}