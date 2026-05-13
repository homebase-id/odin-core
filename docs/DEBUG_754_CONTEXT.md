# DEBUG-754: Stale-pending vs missing-sent puzzle

Scratch context so I can reload state when you come back with logs. **Not for commit.**

## The reported scenario

- **A** has an ICR for **B** (Connected)
- **B** has nothing for **A** (no ICR, no sent-request, no pending-incoming)
- **A** clicks "send connection request" to B
- A's server throws on `CircleNetworkRequestService.cs` line 754 (pre-edit numbering):
  `OdinClientException("The remote identity does not have a corresponding outgoing request.", RemoteServerMissingOutgoingRequest)`
- User says: "in my data, I do not see an incoming request from B"

## Why that's a puzzle

The line-754 throw is reached **only** through `AcceptConnectionRequestAsync`. That method is gated at the top:

```csharp
var incomingRequest = await GetPendingRequestAsync((OdinId)header.Sender, odinContext);
if (null == incomingRequest)
{
    throw new OdinClientException($"No pending request was found from sender [{header.Sender}]",
        OdinClientErrorCode.IncomingRequestNotFound);
}
```

So reaching 754 implies a pending-incoming from B existed in `_pendingRequestValueStorage` at the moment of the call. The user says they don't see one. Three working hypotheses:

1. **The pending-incoming exists but the user isn't looking in the right place.** Pending-incoming is stored in `KeyThreeValue` under context GUID `11e5788a-8117-489e-9412-f2ab2978b46d` (`PendingContextKey`). Not `IdentityConnections`, not the sent-requests context (`27a49f56-…`).
2. **A different code path leads to line 754.** Other entry points into `AcceptConnectionRequestAsync`:
   - V2 controller `PUT /requests/incoming/{senderId}` (UI accept)
   - V1 controller `CircleNetworkRequestsControllerBase.Accept` (UI accept)
   - `HandleConnectionRequestInternalForIdentityOwnerAsync` short-circuit (Send → Accept) — `CircleNetworkRequestService.cs:1009`
   - `HandleConnectionRequestInternalForIntroductionAsync` short-circuit — same file ~`:1058`
   - `HandleConnectionRequestInternalForAppAsync` short-circuit — same file ~`:1120`
   - `CircleNetworkIntroductionService.AutoAcceptAsync` — introduction auto-accept fired by the outbox
3. **Cleanup races.** The pending was there at call time, then deleted by some other path (or another concurrent call) before the user looked.

## Files changed (instrumentation only — revert before committing)

All log lines are tagged `[DEBUG-754]` so they grep cleanly.

### `src/services/Odin.Services/Membership/Connections/Requests/CircleNetworkRequestService.cs`

- **`SendConnectionRequestAsync` entry** — recipient + origin
- **Pre-branch state snapshot** — `icrStatus`, `icrIsConnected`, `hasSentRequest`, `sentOrigin`, `hasPendingIncoming`, `pendingReceivedAt`. *This is the smoking gun for hypothesis #1: if `hasPendingIncoming=true` here, the stale pending exists.*
- **`VerifyConnectionAsync` result** — `isValid`, `remoteWasConnected`
- **IdentityOwner short-circuit branch** — logs when Send is being diverted into Accept (the prime suspect for hitting 754)
- **`AcceptConnectionRequestAsync` entry** — sender, `tryOverrideAcl`, `authContext` (tells us *which* call path got us here)
- **Pending-incoming we're acting on** — sender, `origin`, `introducer`, `receivedAt` (so even if storage is later cleared, the failing call is captured)
- **EstablishConnection failure dump** (the actual line-754 throw site) — `httpStatus`, `httpReason`, `remoteBody`, plus a fresh re-read of local pending/sent/ICR state at the moment of failure
- **`EstablishConnection` (recipient/B side) entry** — caller, `hasSentRequest`, `sentOrigin`, `icrStatus`, `icrIsConnected`. *This is the B-side perspective: shows whether B thinks it ever sent A a request.*

### `src/services/Odin.Services/Membership/Connections/Requests/CircleNetworkIntroductionService.cs`

- **`AutoAcceptAsync` entry** — labels the introduction-auto-accept path so we can tell it apart from owner-Send and explicit accept-incoming UI calls

## What to grab and send back

When you reproduce, capture logs from **both** A's and B's hosts and grep for `DEBUG-754`. The minimum viable set:

**On A's host (the sender)**:
1. `SendConnectionRequestAsync entry` — confirms which API was hit
2. `Pre-branch state` — answers hypothesis #1 (was there really a pending-incoming?)
3. `VerifyConnectionAsync result` — shows whether verification ran
4. Either `IdentityOwner short-circuit` *or* (if not IdentityOwner) the App / Introduction equivalents — which one we'd want to add the same log to, just say so
5. `AcceptConnectionRequestAsync entry` — confirms accept-flow ran and which path triggered it
6. `Acting on pending-incoming` — captures the pending that drove the accept
7. `EstablishConnection failed` — full failure dump

**On B's host (the recipient)**:
1. `EstablishConnection (recipient side) entry` — shows what B saw when A's accept call landed

If `Pre-branch state` shows `hasPendingIncoming=false` but we still hit `AcceptConnectionRequestAsync entry`, the call did **not** come through `SendConnectionRequestAsync` — it's coming directly from the UI's "accept incoming request" endpoint or from the introduction outbox path. The `authContext` field on the accept entry will tell us which.

## Likely outcomes and follow-ups

- **If pending-incoming was there**: confirms my original theory. Fix is at the `HandleConnectionRequestInternalForIdentityOwnerAsync` short-circuit (or wherever): catch the 403 from accept, drop the stale pending-incoming on A, fall through to a fresh `CreateAndSendRequestInternalAsync`.
- **If pending was not there but accept ran via UI**: someone (UI or introduction) is calling accept against a sender for whom A has no pending — that's an upstream bug in whatever decided to call accept.
- **If pending was not there and we never entered accept**: the throw is coming from somewhere else and my line-754 mapping is wrong. Send the full stack trace.

## Cleanup

When done, revert the `[DEBUG-754]` lines:

```bash
git diff -- src/services/Odin.Services/Membership/Connections/Requests/CircleNetworkRequestService.cs \
            src/services/Odin.Services/Membership/Connections/Requests/CircleNetworkIntroductionService.cs
```

…or just `git checkout` those two files once we have the answer.
