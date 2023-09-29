using System;
using Odin.Core.Time;

namespace Odin.Core.Services.Configuration;

public class FirstRunInfo
{
    public static readonly GuidId Key = GuidId.FromString("first_run_key");
    public Int64 FirstRunDate { get; set; }
}


public class EulaSignatureInfo
{
    public static readonly GuidId Key = GuidId.FromString("eula_signature_key");
    public UnixTimeUtc SignatureDate { get; set; }
    public string VersionInfo { get; set; }
}