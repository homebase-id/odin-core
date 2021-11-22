#nullable enable
namespace Youverse.Hosting.Multitenant
{
    public interface ITenantResolutionStrategy
    {
        string? GetTenantIdentifier();
    }
}