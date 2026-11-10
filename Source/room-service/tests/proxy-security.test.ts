import { test } from 'node:test';
import assert from 'node:assert/strict';

test('only Caddy-overwritten valid IP may affect public per-client admission limit', async () => {
  process.env.NETCAFE_TEST = '1';
  process.env.LIVEKIT_API_KEY = 'fake-test-key';
  process.env.LIVEKIT_API_SECRET = 'fake-test-secret';
  const { clientRateLimitIp } = await import('../src/server.ts');
  assert.equal(clientRateLimitIp('172.18.0.2', '198.51.100.12', true), '198.51.100.12');
  assert.equal(clientRateLimitIp('172.18.0.2', '2001:db8::1', true), '2001:db8::1');
  assert.equal(clientRateLimitIp('172.18.0.2', '198.51.100.12, 198.51.100.13', true), '172.18.0.2');
  assert.equal(clientRateLimitIp('172.18.0.2', 'not-an-ip', true), '172.18.0.2');
  assert.equal(clientRateLimitIp('172.18.0.2', '198.51.100.12', false), '172.18.0.2');
});
