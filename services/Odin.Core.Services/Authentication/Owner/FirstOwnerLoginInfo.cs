using Odin.Core.Time;

namespace Odin.Core.Services.Authentication.Owner;

public class FirstOwnerLoginInfo
{
    public static readonly GuidId Key = GuidId.FromString("first-login-key");
    public UnixTimeUtc FirstLoginDate { get; set; }
}