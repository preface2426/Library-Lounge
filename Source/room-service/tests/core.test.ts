import { test } from 'node:test';
import assert from 'node:assert/strict';
import { RoomManager, ApiError, type Gateway } from '../src/core.ts';

function fake() {
  const rooms = new Map<string, Set<string>>();
  const gateway: Gateway = {
    async createRoom(name) { rooms.set(name, new Set()); },
    async deleteRoom(name) { rooms.delete(name); },
    async removeParticipant(name, id) { rooms.get(name)?.delete(id); },
    async listParticipants(name) { if (!rooms.has(name)) throw Error('not found'); return [...rooms.get(name)!]; },
    async getParticipant(name, id) { return rooms.get(name)?.has(id) || false; },
    async token() { return {url:'ws://localhost:7880', token:'test-only', expiresIn:300}; }
  };
  return { gateway, rooms };
}
test('create, join, distinct identity, 6-person capacity and close', async () => {
  const f=fake(), m=new RoomManager(f.gateway);
  const host=await m.create('房主');
  assert.equal(host.role,'host');
  const peers=await Promise.all(Array.from({length:5},(_,i)=>m.join(host.inviteCode,'Guest'+i)));
  assert.equal(new Set([host.participantId,...peers.map(p=>p.participantId)]).size,6);
  await assert.rejects(m.join(host.inviteCode,'Guest6'),(e:unknown)=>e instanceof ApiError && e.status===409);
  await assert.rejects(m.close(host.roomId,peers[0].sessionCredential),(e:unknown)=>e instanceof ApiError && e.status===403);
  const result=await m.close(host.roomId,host.sessionCredential);
  assert.deepEqual(result,{closed:true});
  await assert.rejects(m.join(host.inviteCode,'Late'),(e:unknown)=>e instanceof ApiError && e.status===404);
});
test('token renewal requires session secret and stops after leaving', async () => {
  const m=new RoomManager(fake().gateway);
  const host=await m.create('Host'), guest=await m.join(host.inviteCode,'Guest');
  await assert.rejects(m.renew(host.roomId,'invalid'),(e:unknown)=>e instanceof ApiError && e.status===401);
  assert.equal((await m.renew(host.roomId,guest.sessionCredential)).connection.token,'test-only');
  await m.leave(host.roomId,guest.sessionCredential);
  await assert.rejects(m.renew(host.roomId,guest.sessionCredential),(e:unknown)=>e instanceof ApiError && e.status===401);
});
test('pending expires, reconnect grace and host timeout close', async () => {
  let now=100000;
  const f=fake(), m=new RoomManager(f.gateway,()=>now);
  const host=await m.create('Host');
  const guest=await m.join(host.inviteCode,'Guest');
  now+=61000; await m.reconcile();
  await assert.rejects(m.renew(host.roomId,guest.sessionCredential), (e:unknown)=>e instanceof ApiError && e.status===401);
  assert.equal(m.rooms.size,0);
});
test('active lost guest may refresh during grace but not after expiry', async () => {
  let now=100000; const f=fake(), m=new RoomManager(f.gateway,()=>now);
  const host=await m.create('Host'), guest=await m.join(host.inviteCode,'Guest');
  const room=m.rooms.get(host.roomId)!;
  f.rooms.get(room.livekitName)!.add(host.participantId);
  f.rooms.get(room.livekitName)!.add(guest.participantId);
  await m.confirm(host.roomId,host.sessionCredential);
  await m.confirm(host.roomId,guest.sessionCredential);
  f.rooms.get(room.livekitName)!.delete(guest.participantId);
  await m.reconcile();
  assert.equal((await m.renew(host.roomId,guest.sessionCredential)).connection.expiresIn,300);
  now+=31000; await m.reconcile();
  await assert.rejects(m.renew(host.roomId,guest.sessionCredential));
});
test('reservation blocks concurrent join including pending members', async () => {
  const f=fake(), m=new RoomManager(f.gateway);
  const host=await m.create('Host');
  const results=await Promise.allSettled(Array.from({length:8},(_,i)=>m.join(host.inviteCode,'G'+i)));
  assert.equal(results.filter(r=>r.status==='fulfilled').length,5);
  assert.equal(results.filter(r=>r.status==='rejected').length,3);
  assert.equal(m.rooms.get(host.roomId)?.members.size,6);
});
test('remote removal failure retains a blocked revocation for reconciliation', async () => {
  const f = fake(), m = new RoomManager(f.gateway);
  const host = await m.create('Host');
  const guest = await m.join(host.inviteCode, 'Guest');
  const room = m.rooms.get(host.roomId)!;
  f.rooms.get(room.livekitName)!.add(host.participantId);
  f.rooms.get(room.livekitName)!.add(guest.participantId);
  let fail = true;
  const remove = f.gateway.removeParticipant.bind(f.gateway);
  f.gateway.removeParticipant = async (name, id) => {
    if (fail) throw Error('simulated LiveKit outage');
    return remove(name, id);
  };
  await assert.rejects(m.leave(host.roomId, guest.sessionCredential),
    (e: unknown) => e instanceof ApiError && e.code === 'REMOTE_REMOVAL_UNCONFIRMED');
  assert.equal(room.members.get(guest.participantId)?.state, 'leaving');
  assert.equal(f.rooms.get(room.livekitName)!.has(guest.participantId), true);
  await assert.rejects(m.renew(host.roomId, guest.sessionCredential),
    (e: unknown) => e instanceof ApiError && e.status === 401);
  await m.reconcile(); // Failed remote removal must not restore member to 'active'.
  assert.equal(room.members.get(guest.participantId)?.state, 'leaving');
  fail = false;
  await m.reconcile();
  assert.equal(room.members.has(guest.participantId), false);
  assert.equal(f.rooms.get(room.livekitName)!.has(guest.participantId), false);
});

test('failed remote room deletion closes admission and retries deletion', async () => {
  const f = fake(), m = new RoomManager(f.gateway);
  const host = await m.create('Host');
  const room = m.rooms.get(host.roomId)!;
  const originalDelete = f.gateway.deleteRoom.bind(f.gateway);
  let fail = true;
  f.gateway.deleteRoom = async name => {
    if (fail) throw Error('simulated LiveKit outage');
    await originalDelete(name);
  };
  await assert.rejects(m.close(host.roomId, host.sessionCredential),
    (e: unknown) => e instanceof ApiError && e.code === 'REMOTE_CLOSE_FAILED');
  assert.equal(room.state, 'closing');
  await assert.rejects(m.join(host.inviteCode, 'Late'), (e: unknown) => e instanceof ApiError && e.status === 404);
  await assert.rejects(m.renew(host.roomId, host.sessionCredential),
    (e: unknown) => e instanceof ApiError && e.status === 401);
  await m.reconcile();
  assert.equal(room.state, 'closing');
  fail = false;
  await m.reconcile();
  assert.equal(m.rooms.has(host.roomId), false);
  assert.equal(f.rooms.has(room.livekitName), false);
});

test('presence-provider errors do not incorrectly confirm a session', async () => {
  const f = fake(), m = new RoomManager(f.gateway);
  const host = await m.create('Host');
  f.gateway.getParticipant = async () => { throw Error('LiveKit API unavailable'); };
  await assert.rejects(m.confirm(host.roomId, host.sessionCredential));
  assert.equal(m.rooms.get(host.roomId)!.members.get(host.participantId)?.state, 'pending');
});

