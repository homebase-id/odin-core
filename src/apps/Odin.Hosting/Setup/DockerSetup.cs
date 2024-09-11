using System;
using System.Collections.Generic;
using System.Linq;
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
        
        return 0;
    }
    
    //

    private static void ShowHelp()
    {
        
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