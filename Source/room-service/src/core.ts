import { createHash, randomBytes } from 'node:crypto';

export type MemberState = 'pending' | 'active' | 'reconnecting' | 'leaving';
export type RoomState = 'open' | 'closing' | 'closed';
export type Member = { id: string; displayName: string; state: MemberState; createdAt: number; pendingUntil: number; reconnectUntil: number | null; secretHash: string; };
export type NetRoom = { id: string; livekitName: string; invite: string; inviteExpiresAt: number; hostId: string; state: RoomState; members: Map<string, Member> };
export type Credential = { url: string; token: string; expiresIn: number };
export type JoinResult = { roomId: string; inviteCode: string; participantId: string; role: 'host' | 'member'; sessionCredential: string; connection: Credential };
export interface Gateway {
  createRoom(name: string, capacity: number): Promise<void>;
  deleteRoom(name: string): Promise<void>;
  removeParticipant(name: string, id: string): Promise<void>;
  listParticipants(name: string): Promise<string[]>;
  getParticipant(name: string, id: string): Promise<boolean>;
  token(name: string, id: string, displayName: string): Promise<Credential>;
}
export class ApiError extends Error {
  constructor(public status: number, public code: string) { super(code); }
}
const hex = (bytes = 16) => randomBytes(bytes).toString('hex');
const digest = (value: string) => createHash('sha256').update(value).digest('hex');
export function normalizeName(value: unknown): string {
  if (typeof value !== 'string') throw new ApiError(400, 'INVALID_NICKNAME');
  const name = value.trim();
  if (name.length < 1 || name.length > 24 || /[\u0000-\u001f\u007f]/.test(name))
    throw new ApiError(400, 'INVALID_NICKNAME');
  return name;
}
export class RoomManager {
  readonly rooms = new Map<string, NetRoom>();
  readonly invites = new Map<string, string>();
  private readonly sessions = new Map<string, { roomId: string; memberId: string }>();
  constructor(public readonly gateway: Gateway, private readonly now: () => number = Date.now) {}
  private createMember(name: string) {
    const secret = randomBytes(32).toString('base64url');
    const member: Member = {
      id: hex(12), displayName: name, state: 'pending',
      createdAt: this.now(), pendingUntil: this.now() + 60_000,
      reconnectUntil: null, secretHash: digest(secret)
    };
    return { member, secret };
  }
  private store(room: NetRoom, member: Member) {
    room.members.set(member.id, member);
    this.sessions.set(member.secretHash, { roomId: room.id, memberId: member.id });
  }
  private remove(room: NetRoom, member: Member) {
    room.members.delete(member.id);
    this.sessions.delete(member.secretHash);
  }
  private async result(room: NetRoom, member: Member, secret: string): Promise<JoinResult> {
    return {
      roomId: room.id, inviteCode: room.invite, participantId: member.id,
      role: room.hostId === member.id ? 'host' : 'member',
      sessionCredential: secret,
      connection: await this.gateway.token(room.livekitName, member.id, member.displayName)
    };
  }
  async create(displayName: string): Promise<JoinResult> {
    const name = normalizeName(displayName);
    const id = hex(16), invite = randomBytes(12).toString('base64url');
    const { member, secret } = this.createMember(name);
    const room: NetRoom = {
      id, livekitName: 'netcafe-' + id, invite, inviteExpiresAt: this.now() + 2 * 3600_000,
      hostId: member.id, state: 'open', members: new Map()
    };
    this.rooms.set(id, room); this.invites.set(invite, id); this.store(room, member);
    try {
      await this.gateway.createRoom(room.livekitName, 6);
      return await this.result(room, member, secret);
    } catch {
      this.closed(room);
      try { await this.gateway.deleteRoom(room.livekitName); } catch { /* best effort */ }
      throw new ApiError(502, 'ROOM_CREATION_FAILED');
    }
  }
  async join(code: string, displayName: string): Promise<JoinResult> {
    const name = normalizeName(displayName);
    const id = this.invites.get(code);
    const room = id ? this.rooms.get(id) : undefined;
    if (!room || room.state !== 'open' || room.inviteExpiresAt <= this.now())
      throw new ApiError(404, 'INVALID_INVITATION');
    // Reservation is synchronous: concurrent requests cannot both take the last spot.
    if (room.members.size >= 6) throw new ApiError(409, 'ROOM_FULL');
    const { member, secret } = this.createMember(name);
    this.store(room, member);
    try { return await this.result(room, member, secret); }
    catch { this.remove(room, member); throw new ApiError(502, 'TOKEN_ISSUE_FAILED'); }
  }
  auth(roomId: string, credential: string): {room: NetRoom; member: Member} {
    const rec = this.sessions.get(digest(credential));
    const room = rec && rec.roomId === roomId ? this.rooms.get(roomId) : undefined;
    const member = room && rec ? room.members.get(rec.memberId) : undefined;
    if (!room || room.state !== 'open' || !member || !credential || member.state === 'leaving')
      throw new ApiError(401, 'NOT_AUTHORIZED');
    return {room, member};
  }
  async renew(roomId: string, secret: string) {
    const {room,member} = this.auth(roomId, secret);
    if (member.state === 'pending' && member.pendingUntil < this.now())
      throw new ApiError(410, 'RESERVATION_EXPIRED');
    if (member.state === 'reconnecting' && member.reconnectUntil !== null && member.reconnectUntil < this.now())
      throw new ApiError(410, 'SESSION_EXPIRED');
    return {connection: await this.gateway.token(room.livekitName, member.id, member.displayName)};
  }
  async confirm(roomId: string, secret: string) {
    const {room,member} = this.auth(roomId, secret);
    if (member.state === 'pending' && member.pendingUntil < this.now())
      throw new ApiError(410, 'RESERVATION_EXPIRED');
    if (member.state === 'reconnecting' && member.reconnectUntil !== null && member.reconnectUntil < this.now())
      throw new ApiError(410, 'SESSION_EXPIRED');
    const present = await this.gateway.getParticipant(room.livekitName, member.id);
    if (!present) throw new ApiError(409, 'LIVEKIT_NOT_CONNECTED');
    member.state = 'active'; member.reconnectUntil = null;
    return this.status(roomId, secret);
  }
  status(roomId: string, secret: string) {
    const {room,member} = this.auth(roomId, secret);
    return {roomId: room.id, hostId: room.hostId, participantId: member.id,
      memberCount: room.members.size, members: Array.from(room.members.values()).map(m =>
        ({id: m.id, displayName: m.displayName, state: m.state}))};
  }
  async leave(roomId: string, secret: string) {
    const {room,member} = this.auth(roomId, secret);
    if (member.id === room.hostId) return this.close(roomId, secret);
    // Deny credentials immediately, but retain the member for remote cleanup retries.
    member.state = 'leaving';
    try {
      await this.gateway.removeParticipant(room.livekitName, member.id);
      this.remove(room, member);
    } catch { throw new ApiError(502, 'REMOTE_REMOVAL_UNCONFIRMED'); }
    return {closed: false};
  }
  async close(roomId: string, secret: string) {
    const {room,member} = this.auth(roomId, secret);
    if (member.id !== room.hostId) throw new ApiError(403, 'HOST_ONLY');
    // Closing is a terminal admission state. Preserve the room until remote cleanup succeeds.
    room.state = 'closing';
    this.invites.delete(room.invite);
    try { await this.gateway.deleteRoom(room.livekitName); }
    catch { throw new ApiError(502, 'REMOTE_CLOSE_FAILED'); }
    this.closed(room);
    return {closed: true};
  }
  private closed(room: NetRoom) {
    room.state = 'closed';
    this.invites.delete(room.invite);
    for (const member of room.members.values()) this.sessions.delete(member.secretHash);
    this.rooms.delete(room.id);
    room.members.clear();
  }
  // Called after SDK presence checks. Do not mark players absent if the backend query itself fails.
  async reconcile(): Promise<void> {
    for (const room of [...this.rooms.values()]) {
      if (room.state === 'closing') {
        try { await this.gateway.deleteRoom(room.livekitName); this.closed(room); }
        catch { /* retain for next reconciliation; do not re-admit */ }
        continue;
      }
      let ids: Set<string>;
      try { ids = new Set(await this.gateway.listParticipants(room.livekitName)); }
      catch { continue; }
      for (const member of [...room.members.values()]) {
        if (member.state === 'leaving') {
          if (!ids.has(member.id)) { this.remove(room, member); continue; }
          try { await this.gateway.removeParticipant(room.livekitName, member.id); this.remove(room, member); }
          catch { /* keep this revocation pending and deny fresh credentials */ }
          continue;
        }
        if (ids.has(member.id)) {
          member.state = 'active'; member.reconnectUntil = null;
        } else if (member.state === 'pending') {
          if (this.now() > member.pendingUntil) this.remove(room, member);
        } else if (member.state === 'active') {
          member.state = 'reconnecting'; member.reconnectUntil = this.now() + 30_000;
        } else if (member.reconnectUntil !== null && this.now() > member.reconnectUntil) {
          if (member.id === room.hostId) {
            try { await this.gateway.deleteRoom(room.livekitName); this.closed(room); }
            catch { /* keep room if remote closure cannot be confirmed */ }
            break;
          }
          this.remove(room, member);
        }
      }
      if (room.state === 'open' && !room.members.has(room.hostId)) {
        try { await this.gateway.deleteRoom(room.livekitName); this.closed(room); }
        catch { /* don't claim remote closure */ }
      }
    }
  }
}
