using System;

namespace Youverse.Core.Services.Configuration;

public class FirstRunInfo
{
    public static readonly GuidId Key = GuidId.FromString("first_run_key");
    public UInt64 FirstRunDate { get; set; }
}