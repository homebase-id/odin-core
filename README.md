# HOMEBASE.ID
##### Open Decentralized Identity Network (ODIN)

[![Build](https://github.com/YouFoundation/dotyoucore/actions/workflows/host-build-and-test-main-debug.yml/badge.svg)](https://github.com/YouFoundation/dotyoucore/actions/workflows/host-build-and-test-main-debug.yml)
[![Build](https://github.com/YouFoundation/dotyoucore/actions/workflows/host-build-and-test-main-release.yml/badge.svg)](https://github.com/YouFoundation/dotyoucore/actions/workflows/host-build-and-test-main-release.yml)

####
The Homebase project provides everyone with a fully distributed self-sovereign identity, private communications, and encrypted data storage - owned by you.

- 🚀 Fully distributed, nobody controls the Homebase network.
- 🚀 Private secure communication
- 🚀 Private secure storage
- 🚀 Social Network, including a private secure social network
- 🚀 Personal Link-tree
- 🚀 Personal Homepage with Bio, CV.
- 🚀 Pre-built apps / PWAs for Homebase include Chat, Feed, Photos.
- 🚀 Fully Self-sovereign Identity
- 🚀 Federated Identity Authentication (YouAuth, similar to OAUTH, but more secure)
- 🚀 App Platform
- 🚀 And much more

## Installation of odin-core (Locally)

This odin-core repo is the back-end web server.  If you want to run the front-end apps (chat, feed, etc.), see the repo https://github.com/homebase-id/odin-core.


```bash
clone this repo
dotnet run
```

This repo includes several pre-built identities for local development and testing.

- frodo.dotyou.cloud
- sam.dotyou.cloud
- pippin.dotyou.cloud
- merry.dotyou.cloud

> Their certificates are located in `odin-core/services/Odin.Hosting/https` and are updated every 3 months.  You need  to ensure they're up to date locally.

### Security disclosures
If you discover any security issues, please send an email to security@homebase.id. The email is automatically CCed to the entire team and we'll respond promptly.
