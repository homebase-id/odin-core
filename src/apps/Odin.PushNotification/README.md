# Odin.PushNotification

## Firebase Configuration
Goto https://console.firebase.google.com/

### Create TWO projects for the backend
- One for development / testing
- One for production

Repeat the following steps for each project:

1. Add a new project
2. Give it a name
3. Enable or disable Google Analytics
4. (wait for it to create)
5. Goto Project Settings / Service Accounts
6. On tab *Firebase Admin SDK* click on "Generate new private key"

Save the `development` key in the project dir as `firebase-service-account-key.TEST.json`. 
It is picked up by the appsettings.Development.json file.
The file is already added to .gitignore.

Save the `production` key in a temporary location. It's content must eventually be encrypted by `ansible-vault` and
stored here: https://github.com/YouFoundation/DevOps/blob/main/ansible/files/push-notification/encrypted-firebase-service-account-key.json
It is picked up by the appsettings.ansible-templating.json file when deploying to production using Ansible.

ansible-vault create files/push-notification/encrypted-firebase-service-account-key.json


### Create Android app
Do the following for each project:
1. Goto project overview
2. Add an Android app (look carefully to find it!)
3. Enter the package name (e.g. com.example.myapp)
4. Click "Register app"
5. Follow the rest of the guide to prepare the app source
6. Then go here: https://rnfirebase.io/messaging/usage for the React Native setup.

### Create iOS app
1. Repeat the above instructions for iOS.
2. Then go here: https://rnfirebase.io/messaging/usage/ios-setup#linking-apns-with-fcm-ios

## APNs auth key for VoIP pushes (incoming calls on iOS)

A PushKit VoIP push, the only way to ring an iOS app that is not running, cannot be sent through
Firebase. The relay sends those directly to APNs, and needs an Apple key for it. Everything else
keeps going through Firebase, and the relay runs fine without this section configured: a ring
then goes out as an ordinary alert and the log says `VoIP push wanted … but APNs is not configured`.

What to get, by an admin on the Apple Developer account (no review process):

1. Certificates, Identifiers & Profiles → Keys → add a key with *Apple Push Notifications
   service (APNs)* enabled. Download the `.p8` once (Apple does not offer it again) and note the
   **Key ID**.
2. The **Team ID** from Membership details.
3. The app's **bundle id**. The VoIP topic is `<bundle id>.voip`; the relay derives it.
4. On the app target in Xcode: the Push Notifications capability and the *Voice over IP*
   background mode. The app must report every VoIP push to CallKit immediately.

Then configure the relay (`appsettings*.json` or environment variables):

```json
"Apns": {
  "KeyId": "ABC123DEFG",
  "TeamId": "TEAM000001",
  "KeyFile": "apns-auth-key.p8",
  "BundleId": "id.homebase.chat",
  "Environment": "sandbox"
}
```

- `Environment` is `sandbox` for the dev relay (dev-signed app builds only receive from Apple's
  sandbox host) and `production` for the production relay, mirroring the two Firebase projects.
- The `.p8` is deployed like the Firebase key: never in the image, ansible-vault encrypted in the
  DevOps repo and templated in at deploy time.
- One key serves every app on the team; `BundleId` picks the app.

## Test

Remember when you are testing: iOS emulator cannot do messaging. Use a real device.

For the VoIP path without an Apple key: the token and request builders are unit-tested with a
throwaway key (`tests/apps/Odin.PushNotification.Tests/Apns`), and the iOS app can be exercised
with a simulated push (`xcrun simctl push <device> <bundle id> payload.apns`) carrying the payload
shape documented in `docs/push-delivery-options.md`.