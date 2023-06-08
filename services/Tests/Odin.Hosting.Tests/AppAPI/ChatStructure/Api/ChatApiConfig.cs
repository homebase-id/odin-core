using System;
using Odin.Core.Services.Drives;

namespace Odin.Hosting.Tests.AppAPI.ChatStructure.Api;

public static class ChatApiConfig
{

    public static readonly TargetDrive Drive = new()
    {
        Alias = Guid.Parse("99888555-0000-0000-0000-000000004445"),
        Type = Guid.Parse("11888555-0000-0000-0000-000000001111")
    };
}