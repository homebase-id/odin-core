# DotYouCore

![Build](https://github.com/YouFoundation/DotYouCore/actions/workflows/dotnet.yml/badge.svg)

![Build](https://github.com/YouFoundation/DotYouCore/actions/workflows/ubuntu-build.yml/badge.svg)

![Build](https://github.com/YouFoundation/DotYouCore/actions/workflows/deploy-to-demo.yml/badge.svg)


### Setup

Notes:
* The API tests use frodo.digital and samwise.digital.  Public DNS has these configured to point to 127.0.0.1
* For UI work, you should pull the repos for the react-client-apps and run ```npm install && npm start``` in each.  The web Youverse.Hosting project uses a proxy server to serve these in the dev env.
  * https://github.com/YouFoundation/owner-app
  * https://github.com/YouFoundation/public-app

Steps:
1. Install dotnet sdk v6+
2. Clone this repo: ```git@github.com:YouFoundation/DotYouCore.git```
3. Clone the owner-app: ```git@github.com:YouFoundation/owner-app.git```
4. Clone the public-app: ```git@github.com:YouFoundation/public-app.git```
5. Clone the dotyoucore-transit-lib repo ```git@github.com:YouFoundation/dotyoucore-transit-lib.git```
6. See instructions in owner-app and public-app repos

To run the identity server
1. ```dotyoucore/dotnet restore```
2. ```dotnet run --project Youverse.Hosting/Youverse.Hosting.csproj```

After you have run the public-app and owner-app projects, you can navigate to https://frodo.digital or https://samwise.digital

### Other
#### SSL Notes

Generate New CSR w/o requiring a passphase.

`openssl req -new -newkey rsa:2048 -nodes -keyout private.key -out domain-name.csr`

