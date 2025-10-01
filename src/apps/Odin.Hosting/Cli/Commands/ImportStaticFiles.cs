using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Optimization.Cdn;
using Odin.Services.Registry;
using Odin.Services.Tenant.Container;

namespace Odin.Hosting.Cli.Commands;

public static class ImportStaticFiles
{
    private static readonly SingleKeyValueStorage StaticFileConfigStorage =
        TenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse("3609449a-2f7f-4111-b300-3408a920aa2e"));

    internal static async Task ExecuteAsync(IServiceProvider services)
    {
        var registry = services.GetRequiredService<IIdentityRegistry>();
        var tenantContainer = services.GetRequiredService<IMultiTenantContainer>();

        var logger = services.GetRequiredService<ILogger<CommandLine>>();

        await registry.LoadRegistrations();
        var allTenants = await registry.GetTenants();
        foreach (var tenant in allTenants)
        {
            var scope = tenantContainer.GetTenantScope(tenant.PrimaryDomainName);
            var tenantContext = scope.Resolve<TenantContext>();
            var pm = tenantContext.TenantPathManager;
            var db = scope.Resolve<IdentityDatabase>();

            logger.LogInformation("Tenant: {tenantId} static path: {path}", tenantContext.HostOdinId, pm.StaticPath);

            await Import("sitedata.json", pm.StaticPath, tenantContext, db, logger);
            await Import("public_image.json", pm.StaticPath, tenantContext, db, logger);
            await Import("public_profile.json", pm.StaticPath, tenantContext, db, logger);
        }
    }

    private static async Task Import(string filename, string staticPath, TenantContext tenantContext, IdentityDatabase db, ILogger logger)
    {
        var path = Path.Combine(staticPath, filename);
        if (File.Exists(path) && new FileInfo(path).Length > 0)
        {
            var data = await File.ReadAllBytesAsync(path);

            logger.LogInformation("Tenant: {tenantId} importing: {path} size: {size}", tenantContext.HostOdinId, path, data.Length);

            await StaticFileConfigStorage.UpsertBytesAsync(db.KeyValueCached, GetDataKey(filename), data);
        }
    }

    private static GuidId GetDataKey(string filename)
    {
        return new GuidId(ByteArrayUtil.ReduceSHA256Hash(filename.ToLower() + "_data"));
    }


}