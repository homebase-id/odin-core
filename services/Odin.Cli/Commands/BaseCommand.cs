using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Odin.Core.Services.Configuration;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Odin.Cli.Commands;

public abstract class BaseCommand<T> : Command<T> where T : BaseSettings
{
    // public OdinConfiguration OdinConfig { get; private set; } = null!;
    public CultureInfo HumanReadableCulture { get; }

    protected BaseCommand()
    {
        HumanReadableCulture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        HumanReadableCulture.NumberFormat.NumberGroupSeparator = ",";
        HumanReadableCulture.NumberFormat.NumberDecimalSeparator = ".";
    }

    //

    public string HumanReadableBytes(long bytes)
    {
        string[] sizeSuffixes = { "Bi", "Ki", "Mi", "Gi", "Ti", "Pi", "Ei", "Zi", "Yi" };

        if (bytes == 0)
        {
            return "0" + sizeSuffixes[0];
        }

        var magnitudeIndex = (int)(Math.Log(bytes, 1024));
        var adjustedSize = (decimal)bytes / (1L << (magnitudeIndex * 10));

        return $"{adjustedSize.ToString("N1", HumanReadableCulture)}{sizeSuffixes[magnitudeIndex]}";
    }

    //

    protected abstract int Run(CommandContext context, T settings);

    //

    public override int Execute([NotNull] CommandContext context, [NotNull] T settings)
    {
        // OdinConfig = LoadConfig(settings.Config ?? "", settings.Verbose);
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

        if (verbose)
        {
            AnsiConsole.MarkupLine($"Loading config: [underline]{configFile}[/]");
        }

        if (!File.Exists(configFile))
        {
            throw new Exception($"Config file {configFile} does not exist. Please specify --config.");
        }

        var builder = new ConfigurationBuilder().AddJsonFile(configFile, optional: false, reloadOnChange: false);
        var configuration = builder.Build();


        return new OdinConfiguration(configuration);
    }

    //

}
