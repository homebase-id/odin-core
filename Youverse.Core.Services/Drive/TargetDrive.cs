using System;
using Youverse.Core.Cryptography;

namespace Youverse.Core.Services.Drive;

/// <summary>
///  A drive specifier for incoming requests to perform actions on a drive.  (essentially, this hides the internal DriveId).
/// </summary>
public class TargetDrive
{
    // public Guid Alias { get; set; }
    public ByteArrayId Alias { get; set; }
    public ByteArrayId Type { get; set; }

    public byte[] ToKey()
    {
        return ByteArrayUtil.Combine(Type, Alias);
    }

    public bool IsValid()
    {
        return ByteArrayId.IsValid(this.Alias) && ByteArrayId.IsValid(this.Type);
    }

    public static TargetDrive NewTargetDrive()
    {
        // return new TargetDrive()
        // {
        //     Alias = Guid.NewGuid(),
        //     Type = Guid.NewGuid()
        // };

        return new TargetDrive()
        {
            Alias = (ByteArrayId)ByteArrayUtil.GetRndByteArray(8),
            Type = (ByteArrayId)ByteArrayUtil.GetRndByteArray(8)
        };
    }
}