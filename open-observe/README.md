# OpenObserve ingestion: Caddy site block

Paste the block below into the host's **existing** Caddyfile (the one served by the
`caddy` container you start with `--network host`), then reload Caddy. This is the only
Caddy change the OpenObserve stack needs; everything else lives in the compose file
(`compose.s3.yml` or `compose.local.yml`).

## Copy-paste this

```caddyfile
ingest.logs.homebase.id {
	reverse_proxy h2c://127.0.0.1:5081
}
```

## Before it works

- **5081 is published on loopback.** The compose file publishes OpenObserve's gRPC port as
  `127.0.0.1:5081`. Your Caddy runs with `--network host`, so it shares the host's
  network namespace and reaches that port directly on `127.0.0.1`. (A host-mode container
  can't join a Docker bridge network, so the upstream is the loopback port, not the
  `openobserve` container name.)
- **h2c, not https.** gRPC is HTTP/2 end-to-end. Caddy terminates the real TLS at `:443`
  and forwards HTTP/2 cleartext (`h2c://`) to the upstream, which serves plaintext gRPC.
- **DNS resolves to this host.** The existing Caddy already owns `:80`/`:443` and handles
  ACME, so no `tls`/email directive is needed in this block.

## Reload after editing

```sh
docker exec caddy caddy reload --config /etc/caddy/Caddyfile
```

(Swap `caddy` for your container name if different.)

## UI access

The web UI is deliberately not proxied here. It stays bound to `127.0.0.1:5080` and is
reached over an SSH tunnel, not the public internet:

```sh
ssh -L 5080:127.0.0.1:5080 65.21.248.203   # then open http://localhost:5080
```

See `PLAN.md` for the full design and security model, including §1 "UI access" on why the
tunnel is preferred over exposing the login.
