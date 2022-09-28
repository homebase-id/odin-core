using System;
using Youverse.Core.Services.Drive;

namespace Youverse.Hosting.Tests.AppAPI.Drive.ChatStructure.Api;

public static class ChatApiConfig
{
    public static readonly Guid AppId = Guid.Parse("99888555-4444-0000-4444-000000004444");

    public static readonly TargetDrive Drive = new()
    {
        Alias = Guid.Parse("99888555-0000-0000-0000-000000004445"),
        Type = Guid.Parse("11888555-0000-0000-0000-000000001111")
    };
}