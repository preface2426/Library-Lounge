import { test } from 'node:test';
import assert from 'node:assert/strict';
import { WindowRateLimiter } from '../src/security-limits.ts';

test('fixed-window authenticated request rate limit resets, no secrets stored', () => {
  let clock = 100;
  const limiter = new WindowRateLimiter(3, 1000, 10, () => clock);
  assert.equal(limiter.allow('member-1'), true);
  assert.equal(limiter.allow('member-1'), true);
  assert.equal(limiter.allow('member-1'), true);
  assert.equal(limiter.allow('member-1'), false);
  assert.equal(limiter.allow('member-2'), true);
  clock = 1100;
  assert.equal(limiter.allow('member-1'), true);
  limiter.prune();
  assert.equal(limiter.keyCount, 1);
});

test('the limiter is fail-closed at a hard memory bound', () => {
  let clock = 1000;
  const limiter = new WindowRateLimiter(2, 60_000, 2, () => clock);
  assert.equal(limiter.allow('a'), true);
  assert.equal(limiter.allow('b'), true);
  assert.equal(limiter.allow('c'), false);
  assert.equal(limiter.keyCount, 2);
  clock += 60_000;
  assert.equal(limiter.allow('c'), true);
  assert.equal(limiter.keyCount, 1);
});
