using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Odin.Core.Dto;

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
            Credential = GoogleCredential.FromFile(firebaseCredentialsFile),
        });

        _firebaseMessaging = FirebaseMessaging.GetMessaging(firebaseApp);
    }

    //

    public async Task<string> Post(DevicePushNotificationRequestV1 request)
    {
        var message = new Message
        {
            Token = request.DeviceToken,
            Data = request.ToClientDictionary(),
            Notification = new Notification
            {
                Title = request.Title,
                Body = request.Body,
            },
            Android = new AndroidConfig // magic stuff to increase reliability on android
            {
                Priority = Priority.High,
            },
            Apns = new ApnsConfig // magic stuff to increase reliability on ios
            {
                Headers = new Dictionary<string, string>()
                {
                    { "apns-priority", "10" }
                },
                Aps = new Aps
                {
                    ContentAvailable = true,
                },
            }
        };

        var response = await _firebaseMessaging.SendAsync(message);
        return response;
    }

    //

}
