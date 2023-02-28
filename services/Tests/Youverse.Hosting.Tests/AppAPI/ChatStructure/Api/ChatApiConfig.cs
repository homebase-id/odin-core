using System;
using Youverse.Core.Services.Drives;

namespace Youverse.Hosting.Tests.AppAPI.ChatStructure.Api;

public static class ChatApiConfig
{

    public static readonly TargetDrive Drive = new()
    {
        Alias = Guid.Parse("99888555-0000-0000-0000-000000004445"),
        Type = Guid.Parse("11888555-0000-0000-0000-000000001111")
    };
}