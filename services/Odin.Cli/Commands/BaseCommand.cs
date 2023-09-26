using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Odin.Core.Services.Configuration;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Odin.Cli.Commands;

public abstract class BaseCommand<T> : Command<T> where T : BaseSettings
{
    public OdinConfiguration OdinConfig { get; private set; } = null!;

    //

    protected abstract int Run(CommandContext context, T settings);

    //

    public override int Execute([NotNull] CommandContext context, [NotNull] T settings)
    {
        OdinConfig = LoadConfig(settings.Config ?? "", settings.Verbose);
        return Run(context, settings);
    }

    //

    private static OdinConfiguration LoadConfig(string configFile, bool verbose)
    {
        configFile = configFile.Trim();
        if (configFile == "")
        {
            var assemblyLocation = Assembly.GetExecutingAssembly().Location;
            var directory = Path.GetDirectoryName(assemblyLocation)!;
            configFile = Path.Combine(directory, "appsettings.production.json");
        }

        if (!File.Exists(configFile))
        {
            throw new Exception($"Config file {configFile} does not exist. Please specify --config.");
        }

        var builder = new ConfigurationBuilder().AddJsonFile(configFile, optional: false, reloadOnChange: false);
        var configuration = builder.Build();

        if (verbose)
        {
            AnsiConsole.MarkupLine($"Config file: [underline]{configFile}[/]");
        }

        return new OdinConfiguration(configuration);
    }

    //

}
