using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Core.Logging.LogLevelOverwrite.Serilog;
using Odin.Services.Configuration;
using Odin.Services.Registry;
using Odin.Services.Tenant.Container;
using Serilog;

namespace Odin.Hosting.Cli;

public static class Header2Database
{
    private static IServiceProvider _serviceProvider;
    
    //
    public static void Execute(IEnumerable<string> args)
    {
        Environment.SetEnvironmentVariable("Job__SystemJobsEnabled", "false");
        Environment.SetEnvironmentVariable("Job__TenantJobsEnabled", "false");
        var (odinConfig, appConfig) = AppSettings.LoadConfig(true);

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(appConfig)
            .WriteTo.LogLevelModifier(s => s.Async(sink => sink.Console())).CreateLogger();
        
        var startup = new Startup(appConfig, args);
        
        var services = new ServiceCollection();
        services.AddLogging(loggingBuilder =>
        {
            loggingBuilder.ClearProviders();
            loggingBuilder.AddSerilog();
        });
        
        startup.ConfigureServices(services);

        var serviceProviderFactory = new MultiTenantServiceProviderFactory(
            TenantServices.ConfigureMultiTenantServices,
            TenantServices.InitializeTenant);

        var builder = serviceProviderFactory.CreateBuilder(services);
        startup.ConfigureContainer(builder);
        
        _serviceProvider = serviceProviderFactory.CreateServiceProvider(builder);
        
        var idregs = _serviceProvider.GetRequiredService<IIdentityRegistry>();

        var tenants = idregs.GetTenants().Result;
        foreach (var tenant in tenants)
        {
            ProcessTenant(odinConfig, tenant);
        }
    }
    
    //

    private static void ProcessTenant(OdinConfiguration odinConfig, IdentityRegistration tenant)
    {
        Log.Information(tenant.PrimaryDomainName);
        var tenantHome = Path.Combine(odinConfig.Host.TenantDataRootPath, tenant.PrimaryDomainName);
        var headerFiles = Directory.GetFiles(tenantHome, "*.json", SearchOption.AllDirectories); // Find all .json files
        if (Directory.Exists(tenantHome))
        {
            Log.Error($"Tenant home directory does not exist: {tenantHome}");
            return;
        }
        
    }
    
}