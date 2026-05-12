using Odin.Core.Identity;
using Odin.Hosting.Tests._V2.ApiClient;

namespace Odin.Hosting.Tests.V2.Api;

/// <summary>
/// Common surface for any V2 caller (Owner, App, Guest). Tests parameterized across caller types
/// should accept this and exercise <see cref="Auth"/> plus whatever other V2 wrappers we add later
/// (Drives, Reactions, etc.).
/// </summary>
public interface IV2Caller
{
    OdinId Identity { get; }
    AuthV2Client Auth { get; }
}
