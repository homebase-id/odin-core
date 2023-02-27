using System;

namespace Youverse.Core.Services.Drives.DriveCore.Query.Sqlite;

public class UnprocessedCommandMessage
{
    public Guid Id { get; set; }
    public UnixTimeUtc Received { get; set; }
}