# NetCafe B1 room service (development)

This service only handles invitation-based room admission and LiveKit credentials. It never receives desktop pixels, input events or audio.

## Requirements
- Node.js 22+ (tested target: Node.js 24)
- LiveKit Server running locally or a LiveKit Cloud project
- LIVEKIT_API_KEY and LIVEKIT_API_SECRET stored only on the server

## Local development
1. Run `livekit-server --dev` on a trusted Windows machine. Local defaults: `devkey` / `secret`.
2. In `room-service` run `npm ci`.
3. Set variables from `.env.example` in your shell or a local secret manager. `.env.example` is documentation, not automatically loaded.
4. Run `npm run typecheck` and `npm test`, then `npm start`.
5. Same-machine demo: follow the loopback setup in the [project README](../../README.md). The Unity create/join page uses `http://127.0.0.1:8787` **only when both client and server are on the same physical machine**.
6. Actual second PC: deploy this room-service behind an **HTTPS** reverse proxy with an OS-trusted certificate, use a separately secured LiveKit instance with reachable **WSS** and ICE/TURN media routing, and configure the HTTPS room-service address on **both** Unity create/join pages. The client intentionally rejects plaintext `http://LAN-IP` to prevent bearer credential interception. Never expose local `--dev` keys, open dev endpoints to the internet, or bind the test launcher to `0.0.0.0`.

Do **not** expose the development server and `devkey` / `secret` to the public internet. External testing needs HTTPS, safe credentials, LiveKit Cloud or an appropriately secured LiveKit deployment, and admission limits.

## HTTP API
- POST /v1/rooms — {displayName, requestId}, returns roomId, inviteCode, participantId, sessionCredential, role, connection {url,token,expiresIn}
- POST /v1/rooms/join — {inviteCode, displayName, requestId}, same response
- GET /v1/rooms/{id}/status — Bearer sessionCredential, member list
- POST /v1/rooms/{id}/credentials — Bearer sessionCredential, refreshed scoped connection token
- POST /v1/rooms/{id}/confirm — Bearer sessionCredential, checks presence in LiveKit
- POST /v1/rooms/{id}/leave — Bearer sessionCredential, remove member or close if host
- POST /v1/rooms/{id}/close — Bearer sessionCredential, host only
- GET /healthz — HTTP service liveness only, **not** LiveKit readiness

Sessions are in RAM. Restarting the service invalidates rooms and credentials in its registry. This does not guarantee existing LiveKit rooms were deleted; for production, add durable state/reconciliation before distributed deployment.
### Revocation and remote cleanup (2026-10-31 source change; tests pending)

Guest leave first marks `leaving`, denies credential refresh and retains the member until LiveKit removal is confirmed; periodic reconciliation retries uncertain removals. Host closure enters `closing`, denies room admission and credential renewal, and retries remote deletion if LiveKit is unreachable. A timeout after a successful remote deletion is checked against the LiveKit room/participant listing before considering the cleanup done. All retries operate on the same room identity; they are not additional room creation.

**Important limitation:** revoking this service's sessionCredential does **not** make a previously issued LiveKit JWT immediately invalid on all providers. Self-hosted LiveKit token reentry behavior and Cloud revocation support must be verified before desktop sharing or kicks are treated as secure. The service tests verify its own revocation checks.


A short-lived LiveKit JWT authorizes a room connection, not the room-service API. The separate sessionCredential authorizes room-service operations. Never log, share, or persist either. Participant tokens permit microphone, screen video, and screen-share audio tracks only. Capture starts off and requires explicit user action in the client. Data publishing is permitted for room signaling.

The JWT regression test verifies the signed source allowlist. It does not verify Windows capture, playback, or real cross-device audio quality.

## Verification gaps
The Unity and LiveKit versions must pass an independent Windows x64 build test. A second physical computer and one cross-network test are still required before declaring production-like connectivity complete; the verified prior smoke used two processes on one PC. A Render example is in [../deploy/render.example.yaml](../deploy/render.example.yaml). `npm test` alone does not verify transport connectivity, media ports or reconnect grace periods.
