using Odin.Core.Storage;

namespace Odin.Services.Base;

public static class OdinHeaderNames
{
    //
    // 🚩️ When adding a new header, make sure to update the CorsPolicies.CorsAllowAndExposeHeaders if needed 🚩
    //

    public const string EstablishConnectionAuthToken = "X-DI-EC-ClientAuthToken";
    public const string ClientAuthToken = "X-DI-ClientAuthToken";
        
    /// <summary>
    /// Describes the type of file being uploaded or requested. Values must be A name from <see cref="FileSystemType"/>
    /// </summary>
    public const string FileSystemTypeHeader = "X-ODIN-FILE-SYSTEM-TYPE";

    /// <summary>
    /// Describes the type of file being uploaded or requested. Values must be a name from <see cref="FileSystemType"/>
    /// </summary>
    public const string FileSystemTypeRequestQueryStringName = "xfst";

    public const string RequiresUpgrade = "X-REQUIRES-UPGRADE";
    public const string UpgradeIsRunning = "X-UPGRADE-RUNNING";
        
    public const string RequiresInitialConfiguration = "X-REQUIRES-INITIAL-CONFIGURATION";

    public const string OdinVersionTag = "X-Odin-Version";
    public const string OdinCdnPayload = "X-Odin-Cdn-Payload";

    public const string CorrelationId = "Odin-Correlation-Id";

    //
    // 🚩️ When adding a new header, make sure to update the CorsPolicies.CorsAllowAndExposeHeaders if needed 🚩
    //
}
