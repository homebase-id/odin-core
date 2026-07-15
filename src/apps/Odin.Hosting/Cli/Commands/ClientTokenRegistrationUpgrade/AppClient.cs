using System.Text.Json.Serialization;
using Odin.Core;
using Odin.Services.Authorization.ExchangeGrants;

public sealed class AppClient
{
    public AppClient(GuidId appId, string friendlyName, ServerHalfOfClientKey serverHalfOfClientKey)
    {
        GuidId.AssertIsValid(appId);

        AppId = appId;
        FriendlyName = friendlyName;
        ServerHalfOfClientKey = serverHalfOfClientKey;
    }

    public GuidId AppId { get; init; }

    [JsonPropertyName("accessRegistration")]
    public ServerHalfOfClientKey ServerHalfOfClientKey { get; init; }

    public string FriendlyName { get; init; }
}