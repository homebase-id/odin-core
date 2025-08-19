using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.Database.System.Table;

namespace Odin.Hosting.Cli.Commands;

public static class CreateTestIdentity
{
    internal static async Task ExecuteAsync(IServiceProvider services, Guid identityId, string domain)
    {
        var systemDatabase = services.GetRequiredService<SystemDatabase>();
        await systemDatabase.Registrations.UpsertAsync(new RegistrationsRecord
        {
            identityId = identityId,
            primaryDomainName = domain.ToLower(),
        });
    }
}