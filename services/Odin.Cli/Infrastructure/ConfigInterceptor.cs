using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace Odin.Cli.Infrastructure;

public class ConfigInterceptor : ICommandInterceptor
{
    private readonly IServiceCollection _services;
    public ConfigInterceptor(IServiceCollection services)
    {
        _services = services;
    }

    public void Intercept(CommandContext context, CommandSettings settings)
    {
    }

    //
}