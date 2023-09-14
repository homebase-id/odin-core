#nullable enable
using System;

namespace Odin.Core.Services.Drives;

public class DiskUsage
{
    public Int64 ApproxMetadataBytes { get; set; }
    public Int64 TotalThumbnailBytes { get; set; }
    public Int64 TotalPayloadBytes { get; set; }
    
    public Int64 TotalOtherBytes { get; set; }

    public Int64 GetTotalBytes()
    {
        return ApproxMetadataBytes + TotalThumbnailBytes + TotalPayloadBytes + TotalOtherBytes;
    }
}