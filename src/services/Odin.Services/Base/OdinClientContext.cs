using System;
using Odin.Core;

namespace Odin.Services.Base;

public class OdinClientContext
{
    /// <summary>
    /// The host name used for CORS, if any
    /// </summary>
    public string CorsHostName { get; init; }

    /// <summary>
    /// The app client's access registration id
    /// </summary>
    public GuidId AccessRegistrationId { get; init; }

    public Guid? DevicePushNotificationKey { get; init; }

    public string ClientIdOrDomain { get; set; }

    public OdinClientContext Clone()
    {
        return new OdinClientContext
        {
            CorsHostName = CorsHostName,
            AccessRegistrationId = AccessRegistrationId?.Clone(),
            DevicePushNotificationKey = DevicePushNotificationKey,
            ClientIdOrDomain = ClientIdOrDomain
        };
    }
}