using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Odin.Core.Dto;
using Odin.Core.Logging.CorrelationId;

namespace Odin.PushNotification;

public interface IPushNotification
{
    Task<string> Post(DevicePushNotificationRequest request);
    Task<string> PostAsNotifee(DevicePushNotificationRequest request);
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
            Credential = GoogleCredential.FromFile(firebaseCredentialsFile),
        });

        _firebaseMessaging = FirebaseMessaging.GetMessaging(firebaseApp);
    }

    //

    public async Task<string> Post(DevicePushNotificationRequest request)
    {
        var message = new Message
        {
            Notification = new Notification
            {
                Title = request.Title,
                Body = request.Body,
            },
            Android = new AndroidConfig
            {
                Priority = Priority.High,
            },
            Apns = new ApnsConfig
            {
                Headers = new Dictionary<string, string>()
                {
                    { "apns-priority", "10" },
                },
                Aps = new Aps
                {
                    Alert = new ApsAlert
                    {
                        Title = request.Title,
                        Body = request.Body,
                    },
                    Badge = 99,
                    ContentAvailable = true,
                    // Custom data for the APNs payload can also be added here
                },
                // Optionally, add custom data outside Aps
                CustomData = new Dictionary<string, object>()
                {
                    { "customKey", "customValue" }
                }
            },

            Token = request.DeviceToken,
        };

        var response = await _firebaseMessaging.SendAsync(message);
        return response;
    }

    public async Task<string> PostAsNotifee(DevicePushNotificationRequest request)
    {
        var message = new Message
        {
            Data = new Dictionary<string, string>()
            {
                { "id", Guid.NewGuid().ToString() },
                { "correlationId", request.CorrelationId },
                { "timestamp", DateTime.UtcNow.ToString("o") },
                { "type", "chat" },
                { "message", "Hello, world!" },
            },
            Token = request.DeviceToken,
        };

        var response = await _firebaseMessaging.SendAsync(message);
        return response;
    }
}
