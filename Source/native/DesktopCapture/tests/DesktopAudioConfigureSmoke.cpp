// Exercise the real activation/configuration code without calling IAudioClient::Start.
#include "../DesktopAudio.cpp"

#include <cstdio>

bool StopDiscardsBufferedAudio() {
    const Event stop{CreateEventW(nullptr, TRUE, FALSE, nullptr)};
    if (!stop) return false;
    stopEvent = stop.get();
    state = AudioState::Running;
    const int16_t pcm[] = {16384, 16384, 16384, 16384};
    audioSamples.Push(pcm, 2, GetTickCount64());
    float out = 0.0f;
    const bool hadData = NC_DesktopAudioRead(&out, 1) == 1 && out == 0.5f;
    NC_DesktopAudioStop();
    const bool stopped = NC_DesktopAudioState() == 0 && NC_DesktopAudioRead(&out, 1) == 0 &&
        WaitForSingleObject(stop.get(), 0) == WAIT_OBJECT_0;
    stopEvent = nullptr;
    state = AudioState::Running;
    const bool drained = NC_DesktopAudioRead(&out, 1) == 0;
    NC_DesktopAudioStop();
    return hadData && stopped && drained;
}

int main() {
    if (!StopDiscardsBufferedAudio()) {
        std::fputs("DESKTOP_AUDIO_STOP_FAIL\n", stderr);
        return 1;
    }
    if (!NC_DesktopAudioSupported()) {
        std::fputs("DESKTOP_AUDIO_CONFIGURE_SKIP: unsupported Windows build\n", stdout);
        return 77;
    }
    HRESULT result = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    if (FAILED(result)) return 1;
    {
        const Event stop{CreateEventW(nullptr, TRUE, FALSE, nullptr)};
        const Event ready{CreateEventW(nullptr, FALSE, FALSE, nullptr)};
        ComPtr<IAudioClient> client;
        ComPtr<IAudioCaptureClient> capture;
        result = !stop || !ready ? E_OUTOFMEMORY : GetLoopbackClient(stop.get(), client);
        if (SUCCEEDED(result)) result = ConfigureCapture(*client.Get(), ready.get(), capture);
        if (SUCCEEDED(result) && !capture) result = E_UNEXPECTED;
        if (SUCCEEDED(result)) {
            SetEvent(stop.get());
            ComPtr<IAudioClient> cancelled;
            if (GetLoopbackClient(stop.get(), cancelled) != HRESULT_FROM_WIN32(ERROR_CANCELLED) || cancelled)
                result = E_UNEXPECTED;
        }
    }
    CoUninitialize();
    if (FAILED(result)) {
        std::fprintf(stderr, "DESKTOP_AUDIO_CONFIGURE_FAIL: 0x%08lX\n", static_cast<unsigned long>(result));
        return 1;
    }
    std::puts("DESKTOP_AUDIO_CONFIGURE_AND_STOP_PASS (no capture started)");
    return 0;
}
