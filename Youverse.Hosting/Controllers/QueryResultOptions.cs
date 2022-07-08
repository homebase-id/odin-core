using System;
using Youverse.Core.Services.Drive.Query;

namespace Youverse.Hosting.Controllers;

public class QueryResultOptions
{
    public string StartCursor { get; set; }

    public string StopCursor { get; set; }

    /// <summary>
    /// Max number of records to return
    /// </summary>
    public int MaxRecords { get; set; } = 100;

    public bool IncludeMetadataHeader { get; set; }

    public ResultOptions ToResultOptions()
    {
        return new ResultOptions()
        {
            StartCursor = string.IsNullOrWhiteSpace(this.StartCursor) ? null : Convert.FromBase64String(this.StartCursor),
            StopCursor = string.IsNullOrWhiteSpace(this.StopCursor) ? null : Convert.FromBase64String(this.StopCursor),
            MaxRecords = this.MaxRecords,
            IncludeMetadataHeader = this.IncludeMetadataHeader
        };
    }
}