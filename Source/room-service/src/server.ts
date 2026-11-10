import http from 'node:http';
import { isIP } from 'node:net';
import { createHash } from 'node:crypto';
import { WindowRateLimiter } from './security-limits.ts';
import { AccessToken, RoomServiceClient, TrackSource } from 'livekit-server-sdk';
import { RoomManager, ApiError, type Gateway } from './core.ts';

const url = process.env.LIVEKIT_URL || 'http://127.0.0.1:7880';
const wsUrl = process.env.LIVEKIT_WS_URL || url.replace(/^http/, 'ws');
const apiKey = process.env.LIVEKIT_API_KEY;
const apiSecret = process.env.LIVEKIT_API_SECRET;
if (!apiKey || !apiSecret) throw new Error('Missing LIVEKIT_API_KEY / LIVEKIT_API_SECRET');
const livekit = new RoomServiceClient(url, apiKey, apiSecret);
// Exported solely to verify scoped permissions in regression tests.
export async function issueRoomAccessToken(name: string, id: string, displayName: string): Promise<string> {
  const at = new AccessToken(apiKey, apiSecret, {identity: id, name: displayName, ttl: 300});
  // The client requires explicit consent for each capture; joining a room never starts it.
  // Keep the publishing sources scoped so camera and unspecified sources remain forbidden.
  at.addGrant({room: name, roomJoin: true, canSubscribe: true, canPublish: true,
    canPublishSources: [TrackSource.MICROPHONE, TrackSource.SCREEN_SHARE, TrackSource.SCREEN_SHARE_AUDIO],
    canPublishData: true});
  return at.toJwt();
}
const gateway: Gateway = {
  async createRoom(name, capacity) {
    await livekit.createRoom({name, maxParticipants: capacity});
  },
  async deleteRoom(name) {
    try { await livekit.deleteRoom(name); }
    catch (error) {
      // A timeout can occur after LiveKit removed the room. Confirm before retrying.
      try { if (!(await livekit.listRooms([name])).some(room => room.name === name)) return; }
      catch { /* unknown remote state: keep the cleanup task */ }
      throw error;
    }
  },
  async removeParticipant(name, id) {
    try { await livekit.removeParticipant(name, id); }
    catch (error) {
      // Retire a pending revocation only when the remote participant is proven absent.
      try { if (!(await livekit.listParticipants(name)).some(p => p.identity === id)) return; }
      catch { /* unknown remote state: keep the cleanup task */ }
      throw error;
    }
  },
  async listParticipants(name) {
    return (await livekit.listParticipants(name)).map(p => p.identity);
  },
  async getParticipant(name, id) {
    // A LiveKit outage is not evidence that a participant has left.
    return (await livekit.listParticipants(name)).some(p => p.identity === id);
  },
  async token(name, id, displayName) {
    return {url: wsUrl, token: await issueRoomAccessToken(name, id, displayName), expiresIn: 300};
  }
};
export const manager = new RoomManager(gateway);
const host = process.env.HOST || '127.0.0.1';
const port = Number(process.env.PORT || '8787');
const trustCaddyIp = process.env.TRUST_CADDY_IP === '1';
// Enable only behind Caddy, which overwrites X-Forwarded-For. The private
// room-service container must never publish its HTTP port to the internet.
export function clientRateLimitIp(remoteAddress: string, forwarded: string | string[] | undefined, trustProxy: boolean): string {
  if (!trustProxy || typeof forwarded !== 'string' || !isIP(forwarded)) return remoteAddress;
  return forwarded;
}
// Fail closed for internet-bound demo deployments. Caddy terminates HTTPS
// in front of this private Docker-network listener, while server->LiveKit
// uses HTTPS API and clients receive only WSS WebRTC signaling URLs.
if (process.env.NETCAFE_TEST !== '1' &&
    host !== '127.0.0.1' && host !== '::1' && host !== 'localhost') {
  if (!url.startsWith('https://') || !wsUrl.startsWith('wss://'))
    throw new Error('Non-loopback room service requires HTTPS LiveKit API and WSS clients');
  if (apiKey === 'devkey' || apiSecret === 'secret')
    throw new Error('Refusing public deployment with default LiveKit development credentials');
}

const attempts = new Map<string, number[]>();
const idempotency = new Map<string, {until: number; fingerprint: string; body: object}>();
const inFlight = new Map<string, {fingerprint: string; response: Promise<object>}>();
function limit(ip: string) {
  const now = Date.now();
  const recent = (attempts.get(ip) || []).filter(t => now - t < 60_000);
  if (recent.length >= 12) throw new ApiError(429, 'RATE_LIMITED');
  recent.push(now); attempts.set(ip, recent);
}
function send(res: http.ServerResponse, status: number, body: object) {
  res.writeHead(status, {'Content-Type': 'application/json; charset=utf-8',
    'Cache-Control': 'no-store', 'X-Content-Type-Options': 'nosniff'});
  res.end(JSON.stringify(body));
}
async function readJson(req: http.IncomingMessage): Promise<Record<string, unknown>> {
  const chunks: Buffer[] = []; let size = 0;
  for await (const chunk of req) {
    size += chunk.length;
    if (size > 4096) throw new ApiError(413, 'BODY_TOO_LARGE');
    chunks.push(chunk);
  }
  try {
    const body: unknown = JSON.parse(Buffer.concat(chunks).toString('utf8') || '{}');
    if (!body || typeof body !== 'object' || Array.isArray(body)) throw Error();
    return body as Record<string, unknown>;
  } catch { throw new ApiError(400, 'INVALID_JSON'); }
}
function string(value: unknown): string {
  if (typeof value !== 'string' || value.length > 120) throw new ApiError(400, 'INVALID_FIELD');
  return value;
}
// Readiness logs are intentionally coarse: never log the thrown error or
// request headers, because upstream errors may carry credentials or tokens.
let lastReadinessWarning = 0;
function warnReadinessFailure(error: unknown) {
  const now = Date.now();
  if (now - lastReadinessWarning < 60_000) return;
  lastReadinessWarning = now;
  const e = error && typeof error === 'object' ? error as Record<string, unknown> : {};
  const name = e.name === 'TwirpError' ? 'TwirpError' :
    e.name === 'Error' ? 'Error' : 'Other';
  const rawStatus = e.status ?? e.statusCode;
  const status = typeof rawStatus === 'number' && Number.isInteger(rawStatus) &&
    rawStatus >= 100 && rawStatus <= 599 ? rawStatus : null;
  const rawCode = e.code;
  const code = typeof rawCode === 'string' && /^[a-z_]{1,32}$/.test(rawCode)
    ? rawCode : null;
  console.error('LIVEKIT_READINESS_UNAVAILABLE', JSON.stringify({name,status,code}));
}

// Optional injectable probe is for offline, dummy-only regression tests;
 // the real deployment always uses the privileged LiveKit listRooms client.
export function createHttpServer(readinessProbe: () => Promise<unknown> = () => livekit.listRooms()) {
  // Authenticated status/credential/confirmation reads may reach privileged
  // LiveKit APIs. Leave/close bypass this limiter to preserve emergency
  // revocation and departure. Keep state bounded to avoid memory flooding.
  const memberIpLimit = new WindowRateLimiter(240, 60_000);
  const memberSessionLimit = new WindowRateLimiter(36, 60_000);
  // Share concurrent confirmations for one authenticated member; a single
  // inbound burst must not fan out repeated privileged listParticipants.
  const confirmations = new Map<string, Promise<object>>();
  async function confirmOnce(roomId: string, credential: string): Promise<object> {
    const {member} = manager.auth(roomId, credential);
    const key = roomId + ':' + member.id;
    const existing = confirmations.get(key);
    if (existing) return existing;
    // Bound distinct member confirmations as well (not just each sender).
    if (confirmations.size >= 24) throw new ApiError(429, 'RATE_LIMITED');
    const pending = manager.confirm(roomId, credential);
    confirmations.set(key, pending);
    try { return await pending; }
    finally { if (confirmations.get(key) === pending) confirmations.delete(key); }
  }
  function limitMember(ip: string, roomId: string, credential: string) {
    if (!memberIpLimit.allow(ip)) throw new ApiError(429, 'RATE_LIMITED');
    // Authenticate first so forged Bearer strings never consume a new
    // per-session map slot; use participant ID, never raw credential as key.
    const {member} = manager.auth(roomId, credential);
    if (!memberSessionLimit.allow(roomId + ':' + member.id))
      throw new ApiError(429, 'RATE_LIMITED');
  }
  // A public readiness check must not fan out one privileged LiveKit REST
  // request per unauthenticated HTTP hit. Share concurrent calls and cache a
  // short result; /healthz remains a pure local liveness check.
  let readyInFlight: Promise<boolean> | null = null;
  let readyCache: {value: boolean; expiresAt: number} | null = null;
  async function checkReady(): Promise<boolean> {
    if (readyCache && readyCache.expiresAt > Date.now()) return readyCache.value;
    if (readyInFlight) return readyInFlight;
    readyInFlight = (async () => {
      let timeout: ReturnType<typeof setTimeout> | undefined;
      try {
        await Promise.race([readinessProbe(), new Promise<never>((_, reject) => {
          timeout = setTimeout(() => reject(new Error('LIVEKIT_NOT_READY')), 3000);
          timeout.unref();
        })]);
        readyCache = {value: true, expiresAt: Date.now() + 3000};
        return true;
      } catch (error) {
        warnReadinessFailure(error);
        readyCache = {value: false, expiresAt: Date.now() + 3000};
        return false;
      } finally {
        if (timeout) clearTimeout(timeout);
        readyInFlight = null;
      }
    })();
    return readyInFlight;
  }
  return http.createServer(async (req, res) => {
    try {
      const method = req.method || 'GET';
      const path = new URL(req.url || '/', 'http://localhost').pathname;
      if (method === 'GET' && path === '/healthz') return send(res, 200, {ok: true});
      // /healthz only proves Node is alive; /readyz must reach LiveKit REST.
      if (method === 'GET' && path === '/readyz') {
        const ready = await checkReady();
        return send(res, ready ? 200 : 503, {ready});
      }
      const create = method === 'POST' && path === '/v1/rooms';
      const join = method === 'POST' && path === '/v1/rooms/join';
      if (create || join) {
        const ip = clientRateLimitIp(req.socket.remoteAddress || 'unknown',
          req.headers['x-forwarded-for'], trustCaddyIp);
        limit(ip);
        const body = await readJson(req);
        const requestId = string(body.requestId);
        if (!/^[a-zA-Z0-9_-]{12,100}$/.test(requestId)) throw new ApiError(400, 'INVALID_REQUEST_ID');
        const idKey = ip + ':' + path + ':' + requestId;
        const fingerprint = createHash('sha256').update(JSON.stringify({name: body.displayName, invite: body.inviteCode})).digest('hex');
        const existing = idempotency.get(idKey);
        if (existing && existing.until > Date.now()) {
          if (existing.fingerprint !== fingerprint) throw new ApiError(409, 'REQUEST_ID_REUSED');
          return send(res, 200, existing.body);
        }
        const pending = inFlight.get(idKey);
        if (pending && pending.fingerprint !== fingerprint) throw new ApiError(409, 'REQUEST_ID_REUSED');
        if (!pending) {
          const response = create ? manager.create(string(body.displayName))
            : manager.join(string(body.inviteCode), string(body.displayName));
          inFlight.set(idKey, {fingerprint, response});
          void response.finally(() => { if (inFlight.get(idKey)?.response === response) inFlight.delete(idKey); }).catch(() => {});
        }
        const result = await inFlight.get(idKey)!.response;
        idempotency.set(idKey, {until: Date.now() + 3 * 60_000, fingerprint, body: result});
        return send(res, 200, result);
      }
      const match = path.match(/^\/v1\/rooms\/([a-f0-9]{32})\/(status|credentials|confirm|leave|close)$/);
      if (!match) throw new ApiError(404, 'NOT_FOUND');
      const [, roomId, action] = match;
      const auth = req.headers.authorization || '';
      const credential = auth.startsWith('Bearer ') ? auth.slice(7) : '';
      if (method !== 'POST' && !(method === 'GET' && action === 'status'))
        throw new ApiError(405, 'METHOD_NOT_ALLOWED');
      if (action === 'status' || action === 'credentials' || action === 'confirm') {
        const ip = clientRateLimitIp(req.socket.remoteAddress || 'unknown',
          req.headers['x-forwarded-for'], trustCaddyIp);
        limitMember(ip, roomId, credential);
      }
      if (method === 'GET') return send(res, 200, manager.status(roomId, credential));
      if (action === 'credentials') return send(res, 200, await manager.renew(roomId, credential));
      if (action === 'confirm') return send(res, 200, await confirmOnce(roomId, credential));
      // Never block revocation/leave behind ordinary read rate limits.
      if (action === 'leave') return send(res, 200, await manager.leave(roomId, credential));
      if (action === 'close') return send(res, 200, await manager.close(roomId, credential));
      throw new ApiError(404, 'NOT_FOUND');
    } catch (err) {
      const e = err instanceof ApiError ? err : new ApiError(502, 'SERVICE_UNAVAILABLE');
      if (!(err instanceof ApiError)) console.error('Room service operation failed:', err instanceof Error ? err.name : 'UnknownError');
      send(res, e.status, {error: e.code});
    }
  });
}
if (process.env.NETCAFE_TEST !== '1') {
  const server = createHttpServer();
  server.listen(port, host, () => console.log('NetCafe room service listening on ' + host + ':' + port));
  const timer = setInterval(() => {
    manager.reconcile().catch(() => console.error('Presence reconciliation failed'));
    // Limit retained idempotency keys and rate windows.
    for (const [k, v] of idempotency) if (v.until <= Date.now()) idempotency.delete(k);
    for (const [k, v] of attempts) if (!v.some(t => Date.now() - t < 60_000)) attempts.delete(k);
  }, 5000);
  timer.unref();
}
