import { test } from 'node:test';
import assert from 'node:assert/strict';
import type { Server } from 'node:http';

test('anonymous readiness uses one upstream request per concurrent burst and caches the result', async () => {
  process.env.NETCAFE_TEST = '1';
  process.env.LIVEKIT_API_KEY = 'dummy-readiness-key';
  process.env.LIVEKIT_API_SECRET = 'dummy-readiness-secret';
  process.env.LIVEKIT_URL = 'http://127.0.0.1:1';
  process.env.LIVEKIT_WS_URL = 'ws://127.0.0.1:1';
  const {createHttpServer} = await import('../src/server.ts');
  let upstream = 0;
  const probe = async () => {
    ++upstream;
    await new Promise(resolve => setTimeout(resolve, 55));
    return [];
  };
  const server: Server = createHttpServer(probe);
  await new Promise<void>(resolve => server.listen(0, '127.0.0.1', resolve));
  try {
    const addr=server.address(); if (!addr || typeof addr === 'string') throw Error('address');
    const endpoint='http://127.0.0.1:'+addr.port+'/readyz';
    const burst=await Promise.all(Array.from({length:25},()=>fetch(endpoint)));
    assert.ok(burst.every(r=>r.status===200));
    assert.equal(upstream,1,'simultaneous anonymous checks must be singleflight');
    assert.equal((await fetch(endpoint)).status,200);
    assert.equal(upstream,1,'cached checks must not contact LiveKit again');
    await new Promise(resolve => setTimeout(resolve,3100));
    assert.equal((await fetch(endpoint)).status,200);
    assert.equal(upstream,2,'new requests allowed after bounded cache expiry');
  } finally {
    await new Promise<void>((resolve,reject)=>server.close(e=>e?reject(e):resolve()));
  }
});
