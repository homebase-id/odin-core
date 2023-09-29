using System.ComponentModel;
using Spectre.Console.Cli;

namespace Odin.Cli.Commands.Tenants;

public abstract class TenantsSettings : BaseSettings
{
    [Description("Tenant root directory.")]
    [CommandArgument(0, "<tenant-root-dir>")]
    public string TenantRootDir { get; init; } = "";
}