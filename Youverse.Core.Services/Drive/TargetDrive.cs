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
    public Guid Type { get; set; }

    public byte[] ToKey()
    {
        // return ByteArrayUtil.Combine(Type.ToByteArray(), Alias.ToByteArray());
        return ByteArrayUtil.Combine(Type.ToByteArray(), Alias);

    }

    public bool IsValid()
    {
        //TODO: update to check min lenght
        // return this.Alias != Guid.Empty && this.Type != Guid.Empty;
        return ByteArrayId.IsValid(this.Alias) && this.Type != Guid.Empty;
    }

    public static TargetDrive NewTargetDrive()
    {
        return new TargetDrive()
        {
            Alias = Guid.NewGuid(),
            Type = Guid.NewGuid()
        };
    }
}