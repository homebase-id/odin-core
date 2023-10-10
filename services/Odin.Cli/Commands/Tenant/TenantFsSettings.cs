using System.ComponentModel;
using Odin.Cli.Commands.Base;
using Spectre.Console.Cli;

namespace Odin.Cli.Commands.Tenant;

public abstract class TenantFsSettings : BaseSettings
{
    [Description("Tenant id or domain\nCan include path to tenant root if not current directory")]
    [CommandArgument(0, "<tenant-id-or-domain>")]
    public string TenantIdOrDomain { get; init; } = "";
}