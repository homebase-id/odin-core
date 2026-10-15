using Spectre.Console;

namespace Odin.Cli.Commands.InvitationCode;

internal record InvitationCodeResponse(string Code, string Owner, int MaxUses, int UsesLeft)
{
    public static void Write(InvitationCodeResponse code)
    {
        AnsiConsole.MarkupLineInterpolated($"{code.Code} owner={code.Owner} uses-left={code.UsesLeft}/{code.MaxUses}");
    }
}
