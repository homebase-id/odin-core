namespace Odin.PushNotification.Apns;

/// <summary>
/// Direct APNs access, needed only for PushKit VoIP pushes (a Ring to an iOS device that
/// registered a VoIP token). FCM cannot send those. Everything else keeps going through Firebase.
///
/// NEEDED FROM APPLE before any of this works (an admin on the Apple Developer account; no review):
///   - an APNs authentication key (.p8) from Certificates, Identifiers &amp; Profiles → Keys, with
///     "Apple Push Notifications service (APNs)" enabled: that gives <see cref="KeyId"/> and the
///     file for <see cref="KeyFile"/>;
///   - the account's <see cref="TeamId"/> (Membership details);
///   - the app's <see cref="BundleId"/>; the VoIP topic is "{BundleId}.voip";
///   - on the app target: the Push Notifications capability and the Voice over IP background mode.
/// Until all of these are set, <see cref="IsConfigured"/> is false and a Ring falls back to an
/// ordinary alert push, with a warning in the relay log saying so.
/// </summary>
public class ApnsOptions
{
    public const string SectionName = "Apns";

    public string KeyId { get; set; } = "";
    public string TeamId { get; set; } = "";

    /// <summary>Path to the .p8 key. Deployed like the Firebase key: outside the image, via ansible vault.</summary>
    public string KeyFile { get; set; } = "";

    public string BundleId { get; set; } = "";

    /// <summary>
    /// "sandbox" or "production". Dev-signed app builds only receive from the sandbox host, so the
    /// dev relay uses sandbox and the production relay uses production, mirroring the two Firebase
    /// projects.
    /// </summary>
    public string Environment { get; set; } = "sandbox";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(KeyId) &&
        !string.IsNullOrWhiteSpace(TeamId) &&
        !string.IsNullOrWhiteSpace(KeyFile) &&
        !string.IsNullOrWhiteSpace(BundleId) &&
        File.Exists(KeyFile);

    public Uri Host => Environment.Equals("production", StringComparison.OrdinalIgnoreCase)
        ? new Uri("https://api.push.apple.com")
        : new Uri("https://api.sandbox.push.apple.com");
}
