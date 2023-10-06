using System.ComponentModel;
using Odin.Cli.Commands.Base;
using Spectre.Console.Cli;

namespace Odin.Cli.Commands.Tenants;

public abstract class TenantsSettings : BaseSettings
{
    [Description("Tenant root directory (default: current directory)")]
    [CommandArgument(0, "[tenant-root-dir]")]
    public string TenantRootDir { get; init; } = ".";
}
