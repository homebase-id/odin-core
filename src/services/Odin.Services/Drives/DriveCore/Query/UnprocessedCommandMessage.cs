using System;
using Odin.Core.Time;

namespace Odin.Services.Drives.DriveCore.Query;

public class UnprocessedCommandMessage
{
    public Guid Id { get; set; }
    public UnixTimeUtc Received { get; set; }
}