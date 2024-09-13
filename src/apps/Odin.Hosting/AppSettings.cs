using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Odin.Services.Configuration;

namespace Odin.Hosting;

public static class AppSettings
{
    public static (OdinConfiguration, IConfiguration) LoadConfig(bool includeEnvVars, string configFileOverride = null)
    {
        var configFolder = Environment.GetEnvironmentVariable("ODIN_CONFIG_PATH") ?? Directory.GetCurrentDirectory();
        var aspNetCoreEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? Environments.Production;
        var configSources = new List<string>();
        var configBuilder = new ConfigurationBuilder();

        void AddConfigFile(string fileName, bool optional)
        {
            var appSettingsFile = Path.Combine(configFolder, fileName);
            configSources.Insert(0, appSettingsFile);
            configBuilder.AddJsonFile(appSettingsFile, optional: optional, reloadOnChange: false);
        }

        AddConfigFile("appsettings.json", false); // Common env configuration
        
        if (configFileOverride != null)
        {
            AddConfigFile(configFileOverride, false);
        }
        else
        {
            AddConfigFile($"appsettings.{aspNetCoreEnv.ToLower()}.json", false); // Specific env configuration
            AddConfigFile("appsettings.local.json", true); // Local development overrides
        }

        // Environment variables configuration
        if (includeEnvVars)
        {
            configBuilder.AddEnvironmentVariables();
            configSources.Insert(0, "environment variables");
        }

        try
        {
            var config = configBuilder.Build();
            return (new OdinConfiguration(config), config);
        }
        catch (Exception e)
        {
            var text = $"{e.Message} - check config sources in this order: {string.Join(", ", configSources)}";
            throw new Exception(text, e);
        }
    }
}
