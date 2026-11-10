import { test } from 'node:test';
import assert from 'node:assert/strict';
import type { Server } from 'node:http';

test('HTTP service validates requests without leaking credentials', async () => {
  process.env.NETCAFE_TEST = '1';
  process.env.LIVEKIT_API_KEY = 'fake-test-key';
  process.env.LIVEKIT_API_SECRET = 'fake-test-secret';
  process.env.LIVEKIT_URL = 'http://127.0.0.1:1'; // guaranteed unreachable test-only upstream
  process.env.LIVEKIT_WS_URL = 'ws://127.0.0.1:1';
  const { createHttpServer } = await import('../src/server.ts');
  const server: Server = createHttpServer();
  await new Promise<void>(resolve => server.listen(0, '127.0.0.1', resolve));
  try {
    const addr = server.address(); if (!addr || typeof addr === 'string') throw Error('addr');
    const base = 'http://127.0.0.1:' + addr.port;
    const health = await fetch(base+'/healthz');
    assert.equal(health.status,200);
    assert.equal((await health.json() as {ok:boolean}).ok,true);
    const notReady = await fetch(base+'/readyz');
    assert.equal(notReady.status,503);
    assert.deepEqual(await notReady.json(),{ready:false});
    // Concurrent public readiness requests should share one bounded upstream
    // check, and no request may leak credentials or weaken no-store headers.
    const burst = await Promise.all(Array.from({length: 30}, () => fetch(base+'/readyz')));
    for (const res of burst) {
      assert.equal(res.status,503);
      assert.deepEqual(await res.json(),{ready:false});
      assert.equal(res.headers.get('cache-control'),'no-store');
    }
    const unauth = await fetch(base+'/v1/rooms/'+'a'.repeat(32)+'/status');
    assert.equal(unauth.status,401);
    const invalid = await fetch(base+'/v1/rooms/join',{method:'POST',
      headers:{'content-type':'application/json'},body:JSON.stringify({inviteCode:'bad',displayName:'Guest',requestId:'123456789abc'})});
    assert.equal(invalid.status,404);
    const wrong = await fetch(base+'/v1/rooms/join',{method:'POST',
      headers:{'content-type':'application/json'},body:'not-json'});
    assert.equal(wrong.status,400);
    for(const response of [health,notReady,unauth,invalid,wrong])
      assert.equal(response.headers.get('cache-control'),'no-store');
  } finally {
    await new Promise<void>((resolve,reject)=>server.close(e=>e?reject(e):resolve()));
  }
});
