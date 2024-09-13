using System;
using System.Collections.Generic;
using System.Linq;
using Odin.Core.Configuration;
using Odin.Core.Util;
using Spectre.Console;

namespace Odin.Hosting.Setup;

public static class DockerSetup
{
    public static int Execute(string[] args)
    {
        AnsiConsole.Markup(
            """
            
            [bold green]Homebase[/] table-top Docker setup
            
            """);

        // We can only run on port 80 and 443 for the time being
        const int httpPort = 80;
        const int httpsPort = 443;
        const int adminPort = 4444;
        
        var settings = ParseSettings(args);
        foreach (var setting in settings)
        {
            Console.WriteLine(setting.Key + " = " + setting.Value);
        }
        
        var configFileOverride = settings.GetOrDefault("config-file-override", null);
        var (odinConfig, appSettingsConfig) = AppSettings.LoadConfig(true, configFileOverride);
        var hostConfig = appSettingsConfig.ExportAsEnvironmentDictionary();
        
        //
        // Input image name
        //
        var defaultImageName = settings.GetOrDefault("default-image-name", "ghcr.io/homebase-id/odin-core:latest");
        var prompt = new TextPrompt<string>("Homebase Docker image name").DefaultValue(defaultImageName);
        var imageName = AnsiConsole.Prompt(prompt);
        
        //
        // Input container name
        //
        prompt = new TextPrompt<string>("Docker container name").DefaultValue("identity-host");
        var containerName = AnsiConsole.Prompt(prompt);
        
        //
        // Input root directory volume mount
        //
        var defaultRootDir = settings.GetOrDefault("default-root-dir", null);
        prompt = new TextPrompt<string>("Docker volume mount root directory");
        if (defaultRootDir != null)
        {
            prompt.DefaultValue(defaultRootDir);
        }
        var rootDir = AnsiConsole.Prompt(prompt);
        
        //
        // Run container detached?
        //
        var runDetached = AnsiConsole.Prompt(
            new TextPrompt<bool>("Run container detached?")
                .AddChoice(true)
                .AddChoice(false)
                .DefaultValue(false)
                .WithConverter(choice => choice ? "y" : "n"));
        
        //
        // Configuration overrides
        //
        var provisioningPassword = hostConfig["Registry__InvitationCodes__0"];
        prompt = new TextPrompt<string>("Provisioning password").DefaultValue(provisioningPassword);
        provisioningPassword = AnsiConsole.Prompt(prompt);

        //
        // Construct the Docker command
        //
        var cmd = new List<string>();
        
        cmd.Add($"docker run");
        cmd.Add($"--name {containerName}");
        if (!runDetached)
        {
            cmd.Add($"--rm");
        }
        else
        {
            cmd.Add($"--detach");
            cmd.Add($"--restart always");
        }

        foreach (var keyVal in hostConfig)
        {
            cmd.Add($"--env {keyVal.Key}={keyVal.Value}");
        }
           
        cmd.Add($"--publish {httpPort}:{httpPort}");
        cmd.Add($"--publish {httpsPort}:{httpsPort}");
        cmd.Add($"--publish {adminPort}:{adminPort}");
        
        cmd.Add($"--volume {rootDir}:/homebase");
        cmd.Add($"--pull always");
        cmd.Add($"{imageName}");
        
        var cmdline = string.Join(" \\\n  ", cmd);
        Console.Write(cmdline);
        
        return 0;
    }
    
    //

    private static int ShowHelp()
    {
        AnsiConsole.Markup(
            """
            Arguments:
              config-file-override=<path>  Path to the appsettings.<env>.json file
              default-root-dir=<path>      Default root directory for Docker volume mounts
            """);

        return 1;
    }
    
    //
    
    private static Dictionary<string, string> ParseSettings(string[] args)
    {
        var result = new Dictionary<string, string>();

        var settings = args.Where(x => x.Contains('=')).Select(x => x.Trim(['\'','"'])).ToList();
        foreach (var setting in settings)
        {
            var idx = setting.IndexOf('=');
            if (idx == -1)
            {
                throw new ArgumentException($"Invalid argument: {setting}");
            }

            var key = setting[..idx].ToLower();
            if (key.Length == 0)
            {
                throw new ArgumentException($"Invalid argument: {setting}");
            }
                
            var value = setting[(idx + 1)..];
            if (value.Length == 0)
            {
                throw new ArgumentException($"Invalid argument: {setting}");
            }
    
            result.Add(key, value);            
        }

        return result;
    }
}