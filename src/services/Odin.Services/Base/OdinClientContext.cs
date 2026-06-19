using System;
using Odin.Core;
using Odin.Core.Serialization;

namespace Odin.Services.Base;

public class OdinClientContext : IGenericCloneable<OdinClientContext>
{
    /// <summary>
    /// The host name used for CORS, if any
    /// </summary>
    public string CorsHostName { get; init; }

    /// <summary>
    /// The app client's access registration id
    /// </summary>
    public GuidId AccessRegistrationId { get; init; }

    /// <summary>
    /// The id of the app the caller is acting as, if the caller authenticated with an app token.
    /// Null for owner/guest callers. Used to route app-scoped live data to the correct app's sockets.
    /// </summary>
    public GuidId AppId { get; init; }

    public Guid? DevicePushNotificationKey { get; init; }

    public string ClientIdOrDomain { get; set; }

    public OdinClientContext Clone()
    {
        return new OdinClientContext
        {
            CorsHostName = CorsHostName,
            AccessRegistrationId = AccessRegistrationId?.Clone(),
            AppId = AppId?.Clone(),
            DevicePushNotificationKey = DevicePushNotificationKey,
            ClientIdOrDomain = ClientIdOrDomain
        };
    }
}