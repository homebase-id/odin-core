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
Repeat the above instructions for iOS.

## Test

Remember when you are testing: iOS emulator cannot do messaging. Use a real device.