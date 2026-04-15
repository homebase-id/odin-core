using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.Factory;
using Odin.Services.Configuration;

namespace Odin.Hosting.Cli.Commands;

public static class PgPing
{
    internal static async Task ExecuteAsync(IServiceProvider services)
    {
        var config = services.GetRequiredService<OdinConfiguration>();
        if (config.Database.Type != DatabaseType.Postgres)
        {
            throw new OdinSystemException("Wrong database type");
        }

        var logger = services.GetRequiredService<ILogger<CommandLine>>();
        var db = services.GetRequiredService<SystemDatabase>();
        await using var cn = await db.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT 1";
        var count = (int)(await cmd.ExecuteScalarAsync() ?? 0);
        if (count == 1)
        {
            logger.LogInformation("Postgres connection successful");
        }
    }
}
