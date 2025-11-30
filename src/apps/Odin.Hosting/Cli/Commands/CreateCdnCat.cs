using System;
using Odin.Core;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Hosting.Cli.Commands;

public static class CreateCdnCat
{
    internal static void ExecuteAsync(IServiceProvider services)
    {
        var token = new ClientAuthenticationToken
        {
            Id = Guid.NewGuid(),
            AccessTokenHalfKey = Guid.NewGuid().ToByteArray().ToSensitiveByteArray(),
            ClientTokenType = ClientTokenType.Cdn
        };
        var base64 = token.ToPortableBytes64();
        Console.WriteLine("CDN CAT Token: " + base64);
    }
}