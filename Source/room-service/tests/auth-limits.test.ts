import { test } from 'node:test';
import assert from 'node:assert/strict';
import type { Server } from 'node:http';
import type { Gateway } from '../src/core.ts';

test('authenticated reads are bounded, leave/close remain available, confirm is singleflight', async () => {
  process.env.NETCAFE_TEST = '1';
  process.env.LIVEKIT_API_KEY = 'fake-security-test';
  process.env.LIVEKIT_API_SECRET = 'fake-security-secret';
  process.env.LIVEKIT_URL = 'http://127.0.0.1:1';
  process.env.LIVEKIT_WS_URL = 'ws://127.0.0.1:1';
  const { manager, createHttpServer } = await import('../src/server.ts');
  const rooms = new Map<string, Set<string>>();
  let lookupCalls = 0;
  const gateway: Gateway = manager.gateway;
  gateway.createRoom = async name => { rooms.set(name, new Set()); };
  gateway.deleteRoom = async name => { rooms.delete(name); };
  gateway.removeParticipant = async (name, id) => { rooms.get(name)?.delete(id); };
  gateway.listParticipants = async name => [...(rooms.get(name) ?? [])];
  gateway.getParticipant = async (name, id) => {
    ++lookupCalls;
    await new Promise(resolve => setTimeout(resolve, 40));
    return rooms.get(name)?.has(id) ?? false;
  };
  gateway.token = async () => ({url: 'ws://127.0.0.1:1', token: 'dummy', expiresIn: 300});
  const host = await manager.create('Host'), guest = await manager.join(host.inviteCode, 'Guest');
  const roomName = manager.rooms.get(host.roomId)!.livekitName;
  rooms.get(roomName)!.add(host.participantId);
  rooms.get(roomName)!.add(guest.participantId);
  const server: Server = createHttpServer();
  await new Promise<void>(resolve => server.listen(0, '127.0.0.1', resolve));
  try {
    const address = server.address(); if (!address || typeof address === 'string') throw Error('address');
    const base = 'http://127.0.0.1:' + address.port + '/v1/rooms/' + host.roomId;
    const auth = (secret: string) => ({Authorization: 'Bearer ' + secret});
    // Thirty-six successful host actions; the 37th is rejected without
    // exposing raw token data or calling privileged LiveKit APIs.
    for (let i=0; i<36; ++i) {
      const res = await fetch(base+'/status', {headers: auth(host.sessionCredential)});
      assert.equal(res.status, 200);
    }
    assert.equal((await fetch(base+'/credentials', {method:'POST', headers:auth(host.sessionCredential)})).status, 429);
    assert.equal((await fetch(base+'/status', {headers:auth('forged')})).status, 401);
    // Essential revocation is deliberately not limited by read quotas.
    assert.equal((await fetch(base+'/leave', {method:'POST', headers:auth(guest.sessionCredential)})).status,200);
    assert.equal((await fetch(base+'/close', {method:'POST', headers:auth(host.sessionCredential)})).status,200);
    assert.equal(manager.rooms.has(host.roomId), false);
    // New room, separate HTTP server: real request concurrency uses only one
    // remote confirmation for a member even with multiple simultaneous POSTs.
    const next = await manager.create('NextHost');
    const newName=manager.rooms.get(next.roomId)!.livekitName;
    rooms.get(newName)!.add(next.participantId);
    const again = 'http://127.0.0.1:' + address.port + '/v1/rooms/' + next.roomId;
    const batch = await Promise.all(Array.from({length:8},() => fetch(again+'/confirm',
      {method:'POST',headers:auth(next.sessionCredential)})));
    assert.ok(batch.every(x=>x.status===200));
    assert.equal(lookupCalls,1);
    assert.equal((await fetch(again+'/close',{method:'POST',headers:auth(next.sessionCredential)})).status,200);
  } finally {
    await new Promise<void>((resolve, reject)=>server.close(err=>err?reject(err):resolve()));
  }
});
