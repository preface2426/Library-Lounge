import { test } from 'node:test';
import assert from 'node:assert/strict';
import { TokenVerifier } from 'livekit-server-sdk';

test('issued room JWT permits opt-in mic, screen video and screen audio only', async () => {
  process.env.NETCAFE_TEST = '1';
  process.env.LIVEKIT_API_KEY = 'security-regression-key';
  process.env.LIVEKIT_API_SECRET = 'security-regression-secret';
  const { issueRoomAccessToken } = await import('../src/server.ts');
  const jwt = await issueRoomAccessToken('room-voice-test', 'participant-one', 'Guest');
  const claim = await new TokenVerifier('security-regression-key', 'security-regression-secret').verify(jwt);
  assert.equal(claim.sub, 'participant-one');
  assert.equal(claim.video?.room, 'room-voice-test');
  assert.equal(claim.video?.roomJoin, true);
  assert.equal(claim.video?.canSubscribe, true);
  assert.equal(claim.video?.canPublishData, true);
  assert.equal(claim.video?.canPublish, true);
  // Signed JWT uses canonical string source names, not protobuf numeric enum values.
  assert.deepEqual(claim.video?.canPublishSources, ['microphone', 'screen_share', 'screen_share_audio']);
  await assert.rejects(
    new TokenVerifier('security-regression-key', 'different-secret').verify(jwt),
    /signature|verification|invalid/i
  );
});
