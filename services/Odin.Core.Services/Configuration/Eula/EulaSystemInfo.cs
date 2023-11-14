using System;

namespace Odin.Core.Services.Configuration.Eula;

public static class EulaSystemInfo
{
    // Change when users must sign a new Eula
    public const string RequiredVersion = "05-10-2023";

    //never change
    public static readonly Guid StorageKey = Guid.Parse("df9b4958-0b59-4591-af15-85411d4d90d1");
}