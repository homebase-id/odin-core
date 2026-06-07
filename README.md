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

## Running Locally (back-end + front-end)

This `odin-core` repo is the back-end web server. The front-end apps (owner, chat, feed, mail, community, public) live in a separate repo, https://github.com/homebase-id/odin-js, which you check out next to this one.

This repo ships several pre-built identities for local development and testing:

- frodo.dotyou.cloud
- sam.dotyou.cloud
- pippin.dotyou.cloud
- merry.dotyou.cloud

Their certificates live in `src/apps/Odin.Hosting/https/<domain>/` and are renewed every 3 months. If they've expired, run `src/apps/Odin.Hosting/https/get-certificates.sh` (and `cert-expiry.sh` to check). Below, `frodo.dotyou.cloud` is used as the example identity.

### Prerequisites

1. **.NET 9 SDK** — verify with `dotnet --version` (9.0.x).

2. **Hostnames in `/etc/hosts`** — the dev identities and the front-end dev host must resolve to localhost. Add:

   ```
   127.0.0.1 frodo.dotyou.cloud sam.dotyou.cloud provisioning.dotyou.cloud admin.dotyou.cloud dev.dotyou.cloud
   ```

3. **Permission to bind ports 80/443** — in Development the host listens on 80, 443, and 4444 (admin). On Linux, non-root users can't bind ports below 1024 by default. Either run as root, or lower the unprivileged-port floor once (survives reboots):

   ```bash
   sudo sysctl -w net.ipv4.ip_unprivileged_port_start=80
   echo 'net.ipv4.ip_unprivileged_port_start=80' | sudo tee /etc/sysctl.d/99-odin-low-ports.conf
   ```

   On macOS, ports 80/443 are bindable by normal users, so no action is needed.

### 1. Start the back-end

```bash
# from the repo root
dotnet run --project src/apps/Odin.Hosting
```

This runs in the `Development` environment (the default `launchSettings.json` profile), uses SQLite, and stores data under `$HOME/tmp/dotyou/{system,tenants,logs}` (created automatically). On startup it registers all pre-built identities — you'll see log lines like `Running security health check for frodo.dotyou.cloud`.

Verify it's up:

```bash
curl -sk https://frodo.dotyou.cloud/api/owner/v1/authentication/verifyToken   # -> false
```

> Note: browsing `https://frodo.dotyou.cloud/` directly will 500 until the front-end dev servers (step 2) are running, because the host reverse-proxies the app paths to them.

### 2. Start the front-end (odin-js)

In the `odin-js` repo (checked out next to `odin-core`; Node 18+ works):

```bash
npm install && npm run build:libs   # first time only
npm run start                       # runs all apps concurrently
```

> **Optional `@homebase-id/ffmpeg` dependency.** `js-lib` lazily imports the auth-gated GitHub Packages dependency `@homebase-id/ffmpeg` for video (HLS) processing. `npm install` skips it if you're not authenticated to `npm.pkg.github.com`, but the vite dev server's optimizer still tries to resolve the import at startup and fails with *"@homebase-id/ffmpeg … could not be resolved"* on every app. If you don't need video, provide a tiny no-op stub: create `odin-js/.ffmpeg-stub/` with a `package.json` (`"name": "@homebase-id/ffmpeg"`, `"type": "module"`, `"main": "index.js"`) and an `index.js` that exports a throwing `FFmpeg` class, then symlink `node_modules/@homebase-id/ffmpeg -> ../../.ffmpeg-stub`. (The real package's sources live in the sibling `ffmpeg-kit` / `ffmpeg.wasm` repos if you do need video.)

The Vite dev servers run on `dev.dotyou.cloud` and the back-end reverse-proxies each identity's app paths to them (Development only):

| Path on the identity (e.g. `frodo.dotyou.cloud`) | Proxied to |
| --- | --- |
| `/` (public / home) | `dev.dotyou.cloud:3000` |
| `/owner` | `dev.dotyou.cloud:3001` |
| `/apps/feed` | `dev.dotyou.cloud:3002` |
| `/apps/chat` | `dev.dotyou.cloud:3003` |
| `/apps/mail` | `dev.dotyou.cloud:3004` |
| `/apps/community` | `dev.dotyou.cloud:3006` |
| provisioning | `dev.dotyou.cloud:3005` |

The Vite dev server uses a self-signed `dev.dotyou.cloud` certificate, so your browser will show a TLS warning the first time — accept it.

### 3. Log in as frodo

Open **https://frodo.dotyou.cloud/owner**. On first run you'll set the identity's password; after that you land in the owner console and can experiment (drives, connections, the chat/feed/mail apps, etc.). Repeat with `sam.dotyou.cloud` in a second browser/profile to test peer-to-peer flows between two identities.

## Contributing

Contributions are highly Welcomed 💙 . Feel free to open PRs for small issues such as typos. For large issues or features, please open an issue and wait for it to be assigned to you.

You can reach out to us on our [Discord](https://id.homebase.id/links) server if you have any questions or need help.

## Alpha Version Disclaimer

This is an Alpha version of Homebase.id. Expect breaking changes and significant updates as development progresses. Upgrading will be required during the Alpha phase due to ongoing changes to core functionality. Upgrading may sometimes require running upgrade scripts. Proceed with a production installation only if you're willing to stay up to date and manage the upgrade process as necessary.

### Security disclosures

If you discover any security issues, please send an email to info _at_ homebase _dot_ id. The email is automatically CCed to the entire team and we'll respond promptly.
