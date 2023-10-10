using System.Diagnostics.CodeAnalysis;
using Spectre.Console.Cli;

namespace Odin.Cli.Commands.Base;

public abstract class ApiCommand<T> : BaseCommand<T> where T : ApiSettings
{
    public override int Execute([NotNull] CommandContext context, [NotNull] T settings)
    {
        return base.Execute(context, settings);
    }

}