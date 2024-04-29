using Spectre.Console;
using Spectre.Console.Cli;

namespace Odin.Cli.Commands;

public class DefaultCommand : Command
{
    public override int Execute(CommandContext context)
    {
        AnsiConsole.Write(new Markup("Missing argument. Specify '--help' for more information.\n"));
        return 1;
    }
}
