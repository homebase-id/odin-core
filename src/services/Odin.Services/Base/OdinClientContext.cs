using System;
using System.Collections.Generic;
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

    /// <summary>
    /// The app's locked keys to the zero-knowledge drives it is granted, by drive id; null for a caller that isn't
    /// an app, or an app with none.  Any token of the app gets them in its security context: they open only with
    /// the app's zero-knowledge key (docs/zero-knowledge-drive-api-plan.md, step 3).
    /// </summary>
    public IReadOnlyDictionary<Guid, ZkAppDriveKey> ZkDriveKeys { get; init; }

    /// <summary>
    /// Whether this device holds the app's zero-knowledge key, so counts as holding the key of each drive in
    /// <see cref="ZkDriveKeys"/> (<c>ZkDriveFiles.CallerHoldsDriveKey</c>).
    /// </summary>
    public bool HoldsZkAppKey { get; init; }

    public OdinClientContext Clone()
    {
        return new OdinClientContext
        {
            CorsHostName = CorsHostName,
            AccessRegistrationId = AccessRegistrationId?.Clone(),
            AppId = AppId?.Clone(),
            DevicePushNotificationKey = DevicePushNotificationKey,
            ClientIdOrDomain = ClientIdOrDomain,
            ZkDriveKeys = ZkDriveKeys, // immutable
            HoldsZkAppKey = HoldsZkAppKey
        };
    }
}

/// <summary>
/// A zero-knowledge drive's keys as an app receives them: the drive key locked with the app's zero-knowledge key
/// (<c>aes(AppZkK, DrvK)</c>), and the drive's write-only private key locked with the drive key, both as the browser
/// locked them (base64 of a 12-byte IV, the AES-GCM ciphertext and its tag).
/// </summary>
public sealed record ZkAppDriveKey(string ZkAppKeyEncryptedStorageKey, string ZkStorageKeyEncryptedWriteOnlyPrivateKey);