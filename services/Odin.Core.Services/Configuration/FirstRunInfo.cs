using System;

namespace Odin.Core.Services.Configuration;

public class FirstRunInfo
{
    public static readonly GuidId Key = GuidId.FromString("first_run_key");
    public Int64 FirstRunDate { get; set; }
}