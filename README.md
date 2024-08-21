# ODIN
##### Open Decentralized Identity Network

[![Build](https://github.com/YouFoundation/dotyoucore/actions/workflows/host-build-and-test-main-debug.yml/badge.svg)](https://github.com/YouFoundation/dotyoucore/actions/workflows/host-build-and-test-main-debug.yml)
[![Build](https://github.com/YouFoundation/dotyoucore/actions/workflows/host-build-and-test-main-release.yml/badge.svg)](https://github.com/YouFoundation/dotyoucore/actions/workflows/host-build-and-test-main-release.yml)

####
The ODIN project holds the intent of providing everyone an individual identity, private communications, and encrypted data storage owned by you.

- 🚀 Built-in apps (chat, mail, social feed, photo storage)
- 🚀 Federated Identity (YouAuth, similar to oauth)
- 🚀 App Platform
- 🚀 Much-much more

## Installation (Locally)

This repo is the back-end web server.  If you want to run the front-end apps (chat, feed, etc.), see the repo https://github.com/YouFoundation/dotyoucore-js.


```bash
clone this repo
dotnet run
```

This repo includes several pre-built identities for local development and testing.

- frodo.dotyou.cloud
- sam.dotyou.cloud
- pippin.dotyou.cloud
- merry.dotyou.cloud

> Their certificates are located in `dotyoucore/services/Odin.Hosting/https` and are updated every 3 months.  You need  to ensure they're up to date locally.

### Security disclosures
If you discover any security issues, please send an email to security@homebase.id. The email is automatically CCed to the entire team and we'll respond promptly.
