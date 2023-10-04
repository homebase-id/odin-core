using System;
using Odin.Core.Time;

namespace Odin.Core.Services.Configuration;

public class FirstRunInfo
{
    public static readonly Guid Key = Guid.Parse("50ad871b-604b-47ea-b97c-2d2c3de378ee");
    public Int64 FirstRunDate { get; set; }
}


public class EulaSignatureInfo
{
    public static readonly GuidId Key = GuidId.FromString("eula_signature_key");
    public UnixTimeUtc SignatureDate { get; set; }
    public string VersionInfo { get; set; }
}