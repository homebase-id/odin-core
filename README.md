# Homebase.id

[![Build](https://github.com/YouFoundation/dotyoucore/actions/workflows/host-build-and-test-main.yml/badge.svg)](https://github.com/YouFoundation/dotyoucore/actions/workflows/host-build-and-test-main.yml)

### Setup

Notes:

- The API tests use frodo.dotyou.cloud, sam.dotyou.cloud, merry.dotyou.cloud and pippin.dotyou.cloud. Public DNS has these configured to point to 127.0.0.1. If you want to test offline be sure to add these to your hosts file and be sure to also add them all with the 'capi.' prefix as well (e.g. capi.samwisegamgee.me)
- For UI work, you should pull the monorepo of the react-client-apps and run `npm run start:owner` `npm run start:public` for the owner- and public-app respectively. The web Odin.Hosting project uses a proxy server to serve these in the dev env.
  - https://github.com/YouFoundation/dotyoucore-js
- !!! Serialization
  - Use the DotYouSystemSerializer for all serialization.
    - This contains the global serializer settings needed across the solution
    - If an overload is missing, add it. This uses System.Text.Json.
  - You should not import NewtonsoftJson. (nothing is wrong with it, we just need to stick w/ one library and formatter)

Steps:

1. Install dotnet sdk v6+
2. Clone this repo: `git@github.com:YouFoundation/DotYouCore.git`
3. Clone the repo dotyoucore-lib in a sibling folder to dotyoucore `git@github.com:YouFoundation/dotyoucore-lib.git`
4. Clone the front-end monorepo: `git@github.com:YouFoundation/dotyoucore-js.git`
5. See instructions in dotyoucore-js for running the individual apps

To run the identity server

1. `dotyoucore/dotnet restore`
2. `dotnet run --project Odin.Hosting/Odin.Hosting.csproj`

After you have run the public-app and owner-app projects, you can navigate to https://frodo.dotyou.cloud or https://sam.dotyou.cloud

### Other

#### Preconfigured tenants in dev

- https://frodo.dotyou.cloud/
- https://sam.dotyou.cloud/
- https://merry.dotyou.cloud/
- https://pippin.dotyou.cloud/

#### Provisioning site

- Dev: https://provisioning.dotyou.cloud
- Demo: https://demo.provisioning.id.pub

#### 127.0.0.1 domains

All folders in `dotyoucore/services/Odin.Hosting/https` ending in 'dotyou.cloud' contain certificates for domains with an A record pointing to 127.0.0.1.

Certficates are updated automatically on the demo-box. Download them by running the script `get-certificates.sh`

#### SSL Notes

Generate New CSR w/o requiring a passphase.

`openssl req -new -newkey rsa:2048 -nodes -keyout private.key -out domain-name.csr`

Running from your Mac-M1

- You can run but will not be able to debug due to Sqlite's incompatibility with M1's architecture

`dotnet run --project Odin.Hosting/Odin.Hosting.csproj -r osx-x64`
