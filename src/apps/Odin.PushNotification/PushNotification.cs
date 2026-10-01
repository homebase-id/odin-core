using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Odin.Core.Dto;
using Odin.PushNotification.Apns;

namespace Odin.PushNotification;

public interface IPushNotification
{
    Task<string> Post(DevicePushNotificationRequestV1 request);
}

public class PushNotification : IPushNotification
{
    private readonly FirebaseMessaging _firebaseMessaging;

    public PushNotification(string firebaseCredentialsFile)
    {
        if (!File.Exists(firebaseCredentialsFile))
        {
            throw new FileNotFoundException($"Firebase credentials file not found: {firebaseCredentialsFile}");
        }

        var firebaseApp = FirebaseApp.Create(new AppOptions
        {
            Credential = CredentialFactory.FromFile<ServiceAccountCredential>(firebaseCredentialsFile).ToGoogleCredential()
        });

        _firebaseMessaging = FirebaseMessaging.GetMessaging(firebaseApp);
    }

    //

    public async Task<string> Post(DevicePushNotificationRequestV1 request)
    {
        var message = BuildMessage(request, DateTimeOffset.UtcNow);
        var response = await _firebaseMessaging.SendAsync(message);
        return response;
    }

    //

    /// <summary>
    /// Maps a host request onto one FCM message. Android always gets a data-only, high-priority
    /// message (the app renders its own notification). iOS gets an alert push, or a background push
    /// when <see cref="DevicePushNotificationRequestV1.Silent"/> is set; APNs requires the push type
    /// header to match the payload and priority 5 for background pushes. Sound and badge are left to
    /// the app. Pure, so it can be tested without Firebase credentials.
    /// </summary>
    public static Message BuildMessage(DevicePushNotificationRequestV1 request, DateTimeOffset now)
    {
        var android = new AndroidConfig // magic stuff to increase reliability on android
        {
            Priority = Priority.High,
        };

        var apnsHeaders = new Dictionary<string, string>();
        var aps = new Aps
        {
            ContentAvailable = true,
            MutableContent = true,
        };

        if (request.Silent)
        {
            apnsHeaders["apns-push-type"] = "background";
            apnsHeaders["apns-priority"] = "5";
        }
        else
        {
            apnsHeaders["apns-push-type"] = "alert";
            apnsHeaders["apns-priority"] = "10";
            aps.Alert = new ApsAlert
            {
                Title = request.Title,
                Body = request.Body,
            };
            if (request.TimeSensitive)
            {
                // An interruption level only means something for an alert.
                aps.CustomData = new Dictionary<string, object> { ["interruption-level"] = "time-sensitive" };
            }
        }

        if (request.TimeToLiveSeconds is > 0)
        {
            android.TimeToLive = TimeSpan.FromSeconds(request.TimeToLiveSeconds.Value);
        }

        if (!string.IsNullOrEmpty(request.CollapseId))
        {
            android.CollapseKey = request.CollapseId;
        }

        ApnsHeaders.AddDeliveryHeaders(apnsHeaders, request, now);

        return new Message
        {
            Token = request.DeviceToken,
            Data = request.ToClientDictionary(),
            // Simple notifications taken out, as the RN app is handling them internally
            // Notification = new Notification
            // {
            //     Title = request.Title,
            //     Body = request.Body,
            // },
            Android = android,
            Apns = new ApnsConfig // magic stuff to increase reliability on ios
            {
                Headers = apnsHeaders,
                Aps = aps,
            }
        };
    }
}
