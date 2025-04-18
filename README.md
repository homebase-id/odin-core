# HOMEBASE.ID (ODIN-CORE)

##### Open Decentralized Identity Network (ODIN)

[![Build](https://github.com/homebase-id/odin-core/actions/workflows/host-ci-main-sqlite-debug.yml/badge.svg)](https://github.com/homebase-id/odin-core/actions/workflows/host-ci-main-sqlite-debug.yml)
[![Build](https://github.com/homebase-id/odin-core/actions/workflows/host-ci-main-sqlite-release.yml/badge.svg)](https://github.com/homebase-id/odin-core/actions/workflows/host-ci-main-sqlite-release.yml)
[![Build](https://github.com/homebase-id/odin-core/actions/workflows/host-ci-main-postgres-release.yml/badge.svg)](https://github.com/homebase-id/odin-core/actions/workflows/host-ci-main-postgres-release.yml)

####

The Homebase project provides everyone with a fully distributed self-sovereign identity, private communications, and encrypted data storage - owned by you.

- 🚀 Fully distributed, nobody controls the Homebase network.
- 🚀 Private secure communication
- 🚀 Private secure storage
- 🚀 Social Network, including a private secure social network
- 🚀 Personal Link-tree
- 🚀 Personal Homepage with Bio, CV.
- 🚀 App Platform - included apps: Chat, Feed, Photos.
- 🚀 Fully Self-sovereign Identity (your identity is a domain name!)
- 🚀 Federated Identity Authentication (YouAuth, similar to OAUTH, but more secure)
- 🚀 Optional at-home table-top hosting
- 🚀 Everything is fully encryptable
- 🚀 And much more

## Installation of odin-core (Locally)

This odin-core repo is the back-end web server. If you want to run the front-end apps (chat, feed, etc.), see the repo https://github.com/homebase-id/odin-js.

```bash
clone this repo
dotnet run
```

This repo includes several pre-built identities for local development and testing.

- frodo.dotyou.cloud
- sam.dotyou.cloud
- pippin.dotyou.cloud
- merry.dotyou.cloud

> Their certificates are located in `odin-core/services/Odin.Hosting/https` and are updated every 3 months. You need to ensure they're up to date locally.

## Contributing

Contributions are highly Welcomed 💙 . Feel free to open PRs for small issues such as typos. For large issues or features, please open an issue and wait for it to be assigned to you.

You can reach out to us on our [Discord](https://id.homebase.id/links) server if you have any questions or need help.

## Alpha Version Disclaimer

This is an Alpha version of Homebase.id. Expect breaking changes and significant updates as development progresses. Upgrading will be required during the Alpha phase due to ongoing changes to core functionality. Upgrading may sometimes require running upgrade scripts. Proceed with a production installation only if you're willing to stay up to date and manage the upgrade process as necessary.

### Security disclosures

If you discover any security issues, please send an email to info _at_ homebase _dot_ id. The email is automatically CCed to the entire team and we'll respond promptly.
