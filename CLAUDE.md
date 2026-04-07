# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

ODIN-CORE (Homebase.ID) is a decentralized identity platform providing self-sovereign identity, encrypted storage, and peer-to-peer communication. Each identity owns a domain and runs its own server. Built on .NET 9.0 targeting both SQLite and PostgreSQL.

## Build & Test Commands

```bash
# Build
dotnet build ./odin-core.sln

# Run all tests
dotnet test ./odin-core.sln

# Run a specific test project
dotnet test ./tests/core/Odin.Core.Tests/Odin.Core.Tests.csproj

# Run a single test by fully qualified name
dotnet test --filter "FullyQualifiedName=Namespace.Class.Method"

# Run the identity host server
dotnet run --project src/apps/Odin.Hosting

# Start dev PostgreSQL + Redis (for non-SQLite testing)
docker/start-dev-servers.sh
```

Test framework is **NUnit** across 10 test projects (~2,000+ tests total). Tests are under `tests/` mirroring the `src/` structure. Integration tests in `Odin.Hosting.Tests` (~933 tests, the largest project) use `WebScaffold` for test server setup and pre-built test identities (frodo, sam, pippin, merry `.dotyou.cloud`).

**Note:** `dotnet test` CLI runs in NUnit "Non-Explicit" mode, which excludes `[Explicit]` tests. The CLI total will be lower than Visual Studio Test Explorer's count. This is expected — not missing tests.

### Writing Integration Tests (Odin.Hosting.Tests)

Two test infrastructures exist side by side:

- **`_Universal/`** -- V1 API tests. Uses `OwnerApiClientRedux` which bundles all clients (`DriveRedux`, `Connections`, `DriveManager`) with helpers like `WaitForEmptyOutbox()`, `ProcessInbox()`, `SendReadReceipt()`. Peer-to-peer flows (transfers, read receipts, reactions) are well-supported here.

- **`_V2/`** -- V2 API tests. Uses separate `DriveReaderV2Client` and `DriveWriterV2Client`. These require manual construction:
  1. Create an `OwnerTestCase(targetDrive)` (or `AppTestCase`, `GuestTestCase`)
  2. Call `await callerContext.Initialize(ownerApiClient)` -- **must be called before `GetFactory()`**
  3. Create client: `new DriveReaderV2Client(identity.OdinId, callerContext.GetFactory())`

  V2 clients lack outbox/inbox helpers. For peer-to-peer flows in V2 tests, fall back to `OwnerApiClientRedux.DriveRedux` for `WaitForEmptyOutbox()`, `ProcessInbox()`, and `SendReadReceipt()`, or add the test to an existing `_Universal/` test class where the infrastructure works.

**Peer file transfer pattern** (for tests that send files between identities):
1. Create drives on both sender and recipient with the same `TargetDrive`
2. Create a circle on the recipient granting `DrivePermission.Write` on the target drive
3. Connect: sender sends connection request, recipient accepts into the circle
4. Upload with `TransitOptions { Recipients = [...], AllowDistribution = true }`
5. `WaitForEmptyOutbox()` on sender, then `ProcessInbox()` on recipient
6. Find recipient's copy via `QueryByGlobalTransitId(uploadResult.GlobalTransitIdFileIdentifier)` -- the recipient has a **different FileId** than the sender

**Test identities**: `TestIdentities.Frodo`, `TestIdentities.Samwise`, etc. Each `WebScaffold` instance runs its own server. Test classes that need peer flows must include at least two identities in `RunBeforeAnyTests()`.

## Architecture

### Layer Structure

```
src/core/       -> Odin.Core, Odin.Core.Cryptography, Odin.Core.Storage
src/services/   -> Odin.Services (business logic)
src/apps/       -> Odin.Hosting (main web server), Odin.Cli, and supporting services
```

Dependencies flow downward: Hosting -> Services -> Core. Never reference upward.

### Multi-Tenancy

One tenant = one identity (domain). Tenant resolution happens from the HTTP host header. Per-tenant DI containers via `MultiTenantServiceProviderFactory`. All service methods receive `IOdinContext` which carries tenant, caller, and permission information -- never use `HttpContext` directly in services.

### Database

No ORM. Raw SQL with a code-first table definition pattern. Tables are classes with CRUD methods (e.g., `TableAppNotificationsCRUD.cs`). Database factories: `SqliteIdentityDbConnectionFactory` or `PgsqlIdentityDbConnectionFactory`. Migrations use `AbstractMigrator`. Both SQLite and PostgreSQL are tested in CI.

### Authentication & API Versions

Four auth schemes, each with its own controller directory:
- **Owner** (`/OwnerToken/`) -- identity owner logged into console
- **YouAuth/App** (`/ClientToken/App/`) -- owner logged into an app
- **Guest** (`/ClientToken/Guest/`) -- connected peer browsing
- **Peer CAPI** (`/PeerIncoming/`) -- server-to-server via client certificates

**V1 API** controllers live in `Controllers/{Base,OwnerToken,ClientToken}/` with route prefix `/api/owner/v1/`, `/api/apps/v1/`, etc.

**V2 API** (unified) lives in `UnifiedV2/` with route prefix `/api/v2/`. V2 uses `IAuthPathHandler` to unify auth behind a single endpoint.

### Drives & File System

The core storage abstraction. Each identity has drives containing files. `IDriveFileSystem` composes `DriveQueryServiceBase` (reads) and `DriveStorageServiceBase` (writes). File identity: `InternalDriveFileId = (DriveId: Guid, FileId: Guid)`. Two file system types: `Standard` and `Comment`.

### Peer-to-Peer

Identities connect via **Circles** (connection groups). Transit system handles encrypted file/message transport. Outgoing requests go through `PeerOutgoingTransferService` -> outbox -> background workers -> HTTP to peer. Incoming requests land in transit inbox -> `PeerInboxProcessor`. Read receipts, file transfers, and delete notifications all use this pattern.

### Events

MediatR for in-process pub/sub. Key notifications: `DriveFileAddedNotification`, `DriveFileChangedNotification`, `DriveFileDeletedNotification`. Handlers update caches, propagate to peers, and push WebSocket notifications.

### DI Registration

Autofac + Microsoft DI. System-level services registered in `Odin.Hosting/SystemServices.cs`. Tenant-level services in `TenantServices.ConfigureTenantServices`.

### Cryptography

Custom crypto layer in `Odin.Core.Cryptography`. AES-GCM (preferred) and AES-CBC (legacy). Key exchange via ECC. BIP39 mnemonic generation for recovery keys.

## Key Types

- `OdinId` -- value type wrapping a domain name (e.g., `frodo.dotyou.cloud`)
- `IOdinContext` -- request context with tenant, caller, permissions
- `InternalDriveFileId` -- `(DriveId, FileId)` tuple identifying a file
- `UnixTimeUtc` -- millisecond-precision UTC timestamp used throughout
- `ServerFileHeader` -- encrypted file header with metadata
- `LocalAppMetadata` -- per-file metadata that stays local (never sent to peers)

## CI / CD
Do NOT use slash (/) in Git branch names.

