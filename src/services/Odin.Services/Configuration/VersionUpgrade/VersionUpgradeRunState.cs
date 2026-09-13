namespace Odin.Services.Configuration.VersionUpgrade;

/// <summary>
/// Tracks whether a version upgrade is currently running for this tenant.
/// <para>
/// This is a tenant-level singleton with no dependencies, for two reasons. First, the upgrade runs on a
/// background job in its own lifetime scope, so a flag held on the (per-scope) <see cref="VersionUpgradeService"/>
/// is not visible to the request scope that <c>VersionUpgradeMiddleware</c> asks. Second, that middleware
/// asks on every /api request, and resolving <see cref="VersionUpgradeService"/> to read a bool means
/// building its whole dependency graph each time.
/// </para>
/// </summary>
public sealed class VersionUpgradeRunState
{
    private volatile bool _isRunning;

    public bool IsRunning => _isRunning;

    public void SetRunning(bool isRunning)
    {
        _isRunning = isRunning;
    }
}
