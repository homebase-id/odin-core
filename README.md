# DotYouCore

![Build](https://github.com/YouFoundation/DotYouCore/actions/workflows/lib-on-ubuntu.yml/badge.svg)

![Build](https://github.com/YouFoundation/DotYouCore/actions/workflows/services-on-ubuntu.yml/badge.svg)

### Setup

Notes:
* The API tests use frodo.digital and samwise.digital.  Public DNS has these configured to point to 127.0.0.1
* For UI work, you should pull the repos for the react-client-apps and run ```npm install && npm start``` in each.  The web Youverse.Hosting project uses a proxy server to serve these in the dev env.
  * https://github.com/YouFoundation/owner-app
  * https://github.com/YouFoundation/public-app
* !!! Serialization
  * Use the DotYouSystemSerializer for all serialization.  
    * This contains the global serializer settings needed across the solution
    * If an overload is missing, add it.  This uses System.Text.Json.
  * You should not import NewtonsoftJson. (nothing is wrong with it, we just need to stick w/ one library and formatter)

Steps:
1. Install dotnet sdk v6+
2. Clone this repo: ```git@github.com:YouFoundation/DotYouCore.git```
3. Clone the repo dotyoucore-lib in a sibling folder to dotyoucore ```git@github.com:YouFoundation/dotyoucore-lib.git```
4. Clone the owner-app: ```git@github.com:YouFoundation/owner-app.git```
5. Clone the public-app: ```git@github.com:YouFoundation/public-app.git```
6. Clone the dotyoucore-transit-lib repo ```git@github.com:YouFoundation/dotyoucore-transit-lib.git```
7. See instructions in owner-app and public-app repos

To run the identity server
1. ```dotyoucore/dotnet restore```
2. ```dotnet run --project Youverse.Hosting/Youverse.Hosting.csproj```

After you have run the public-app and owner-app projects, you can navigate to https://frodo.digital or https://samwise.digital

### Other
#### SSL Notes

Generate New CSR w/o requiring a passphase.

`openssl req -new -newkey rsa:2048 -nodes -keyout private.key -out domain-name.csr`

