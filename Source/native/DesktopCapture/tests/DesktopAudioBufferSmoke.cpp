#include "../DesktopAudioBuffer.h"

#include <array>
#include <cassert>
#include <cstdio>

int main() {
    DesktopAudioBuffer buffer;
    const int16_t stereo[] = {32767, -32768, 16384, 16384, -32768, -32768};
    float output[4]{};

    buffer.Push(stereo, 3, 1);
    assert(buffer.Read(output, 2, 2) == 2);
    assert(output[0] == -1.0f / 65536 && output[1] == 0.5f);
    buffer.Push(nullptr, 1, 3);
    assert(buffer.Read(output, 4, 4) == 2);
    assert(output[0] == -1.0f && output[1] == 0.0f);

    std::array<int16_t, 4900 * 2> backlog{};
    for (size_t i = 0; i < 4900; ++i) backlog[i * 2] = 16384;
    backlog[100 * 2] = 8192;
    buffer.Push(backlog.data(), 4900, 5);
    assert(buffer.Read(output, 1, 6) == 1);
    assert(output[0] == 0.125f); // Drop the first 100 frames of an oversized packet.
    buffer.Push(stereo, 3, 7);
    assert(buffer.Read(output, 1, 8) == 1);
    assert(output[0] == 0.25f); // Drop old queued frames as fresh packets arrive.
    assert(buffer.Read(output, 4, 108) == 0); // No stale playback after capture stalls.

    buffer.Push(stereo, 3, 200);
    buffer.Push(nullptr, 1, 290);
    assert(buffer.Read(output, 4, 310) == 1 && output[0] == 0.0f); // Drop old packets even during sparse output.

    buffer.Push(stereo, 3, 400);
    buffer.Clear();
    assert(buffer.Read(output, 4, 401) == 0); // Stopped/replaced session cannot leak old audio.
    buffer.Push(nullptr, 1, 402);
    assert(buffer.Read(output, 4, 403) == 1 && output[0] == 0.0f);
    std::puts("DESKTOP_AUDIO_BUFFER_PASS");
}
