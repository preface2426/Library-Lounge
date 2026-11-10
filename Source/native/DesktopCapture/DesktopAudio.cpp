#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <audioclient.h>
#include <audioclientactivationparams.h>
#include <mmdeviceapi.h>
#include <process.h>
#include <winternl.h>
#include <wrl/implements.h>

#include <atomic>
#include <cstring>
#include <memory>
#include "DesktopAudioBuffer.h"

#define API extern "C" __declspec(dllexport)

namespace {
using Microsoft::WRL::ClassicCom;
using Microsoft::WRL::ComPtr;
using Microsoft::WRL::FtmBase;
using Microsoft::WRL::Make;
using Microsoft::WRL::RuntimeClass;
using Microsoft::WRL::RuntimeClassFlags;

enum class AudioState : int { Failed = -1, Stopped = 0, Initializing = 1, Running = 2 };
constexpr DWORD ActivationTimeoutMs = 5000;

SRWLOCK gate = SRWLOCK_INIT;
AudioState state = AudioState::Stopped;
uint64_t generation = 0;
bool workerActive = false;
HANDLE stopEvent = nullptr;
DesktopAudioBuffer audioSamples;
std::atomic_bool activationPending = false;

class AudioLock {
public:
    AudioLock() noexcept { AcquireSRWLockExclusive(&gate); }
    ~AudioLock() { ReleaseSRWLockExclusive(&gate); }
    AudioLock(const AudioLock&) = delete;
    AudioLock& operator=(const AudioLock&) = delete;
};

struct CloseEvent {
    void operator()(HANDLE event) const noexcept { CloseHandle(event); }
};
using Event = std::unique_ptr<void, CloseEvent>;

class Activation final : public RuntimeClass<RuntimeClassFlags<ClassicCom>, FtmBase,
                                            IActivateAudioInterfaceCompletionHandler> {
public:
    Activation() noexcept {
        parameters_.ActivationType = AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK;
        parameters_.ProcessLoopbackParams.TargetProcessId = GetCurrentProcessId();
        parameters_.ProcessLoopbackParams.ProcessLoopbackMode = PROCESS_LOOPBACK_MODE_EXCLUDE_TARGET_PROCESS_TREE;
        properties_.vt = VT_BLOB;
        properties_.blob.cbSize = sizeof(parameters_);
        properties_.blob.pBlobData = reinterpret_cast<BYTE*>(&parameters_);
    }

    HRESULT Activate(ComPtr<IActivateAudioInterfaceAsyncOperation>& operation) noexcept {
        return ActivateAudioInterfaceAsync(VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK, __uuidof(IAudioClient),
                                           &properties_, this, operation.GetAddressOf());
    }

    HRESULT STDMETHODCALLTYPE ActivateCompleted(IActivateAudioInterfaceAsyncOperation*) noexcept override {
        activationPending.store(false);
        SetEvent(complete.get());
        return S_OK;
    }

    Event complete{CreateEventW(nullptr, FALSE, FALSE, nullptr)};

private:
    AUDIOCLIENT_ACTIVATION_PARAMS parameters_{};
    PROPVARIANT properties_{};
};

HRESULT GetLoopbackClient(HANDLE stop, ComPtr<IAudioClient>& client) noexcept {
    if (WaitForSingleObject(stop, 0) != WAIT_TIMEOUT) return HRESULT_FROM_WIN32(ERROR_CANCELLED);
    const auto callback = Make<Activation>();
    if (!callback || !callback->complete) return E_OUTOFMEMORY;
    // An abandoned OS activation may finish later. Do not queue another one until it does.
    if (activationPending.exchange(true)) return HRESULT_FROM_WIN32(ERROR_BUSY);
    ComPtr<IActivateAudioInterfaceAsyncOperation> operation;
    const HRESULT activation = callback->Activate(operation);
    if (FAILED(activation)) {
        activationPending.store(false);
        return activation;
    }
    const HANDLE events[] = {stop, callback->complete.get()};
    switch (WaitForMultipleObjects(2, events, FALSE, ActivationTimeoutMs)) {
    case WAIT_OBJECT_0: return HRESULT_FROM_WIN32(ERROR_CANCELLED);
    case WAIT_OBJECT_0 + 1: break;
    case WAIT_TIMEOUT: return HRESULT_FROM_WIN32(ERROR_TIMEOUT);
    default: return HRESULT_FROM_WIN32(GetLastError());
    }
    HRESULT result = E_UNEXPECTED;
    ComPtr<IUnknown> activated;
    const HRESULT readResult = operation->GetActivateResult(&result, activated.GetAddressOf());
    if (FAILED(readResult)) return readResult;
    if (FAILED(result)) return result;
    return activated ? activated.As(&client) : E_UNEXPECTED;
}

HRESULT ConfigureCapture(IAudioClient& client, HANDLE ready, ComPtr<IAudioCaptureClient>& capture) noexcept {
    constexpr WORD Channels = 2;
    constexpr WORD BlockAlign = Channels * sizeof(int16_t);
    const WAVEFORMATEX format{WAVE_FORMAT_PCM, Channels, DesktopAudioBuffer::SampleRate,
                              DesktopAudioBuffer::SampleRate * BlockAlign, BlockAlign, 16, 0};
    HRESULT result = client.Initialize(AUDCLNT_SHAREMODE_SHARED, AUDCLNT_STREAMFLAGS_LOOPBACK |
        AUDCLNT_STREAMFLAGS_EVENTCALLBACK | AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM,
        0, 0, &format, nullptr);
    if (FAILED(result)) return result;
    result = client.SetEventHandle(ready);
    if (FAILED(result)) return result;
    return client.GetService(IID_PPV_ARGS(capture.GetAddressOf()));
}

bool CapturePackets(IAudioCaptureClient& capture, HANDLE stop, uint64_t session) noexcept {
    while (WaitForSingleObject(stop, 0) == WAIT_TIMEOUT) {
        UINT32 frames = 0;
        if (FAILED(capture.GetNextPacketSize(&frames))) return false;
        if (!frames) return true;
        BYTE* pcm = nullptr;
        DWORD flags = 0;
        const HRESULT result = capture.GetBuffer(&pcm, &frames, &flags, nullptr, nullptr);
        if (FAILED(result)) return false;
        if (result == AUDCLNT_S_BUFFER_EMPTY) return true;
        {
            const AudioLock lock;
            if (generation == session && state == AudioState::Running) {
                if (flags & AUDCLNT_BUFFERFLAGS_DATA_DISCONTINUITY) audioSamples.Clear();
                audioSamples.Push(flags & AUDCLNT_BUFFERFLAGS_SILENT ? nullptr : pcm, frames, GetTickCount64());
            }
        }
        if (FAILED(capture.ReleaseBuffer(frames))) return false;
    }
    return false;
}

void RunCapture(HANDLE stop, uint64_t session) noexcept {
    const Event ready{CreateEventW(nullptr, FALSE, FALSE, nullptr)};
    ComPtr<IAudioClient> client;
    ComPtr<IAudioCaptureClient> capture;
    if (!ready || FAILED(GetLoopbackClient(stop, client)) ||
        WaitForSingleObject(stop, 0) != WAIT_TIMEOUT ||
        FAILED(ConfigureCapture(*client.Get(), ready.get(), capture)) ||
        WaitForSingleObject(stop, 0) != WAIT_TIMEOUT || FAILED(client->Start())) return;

    bool published = false;
    {
        const AudioLock lock;
        if (generation == session && state == AudioState::Initializing) {
            state = AudioState::Running;
            published = true;
        }
    }

    if (published) {
        const HANDLE events[] = {stop, ready.get()};
        while (WaitForMultipleObjects(2, events, FALSE, INFINITE) == WAIT_OBJECT_0 + 1 &&
               CapturePackets(*capture.Get(), stop, session)) {}
    }
    client->Stop();
}

unsigned __stdcall Worker(void*) noexcept {
    const HRESULT com = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    for (;;) {
        uint64_t session = 0;
        const Event stop{CreateEventW(nullptr, TRUE, FALSE, nullptr)};
        {
            const AudioLock lock;
            if (state != AudioState::Initializing || FAILED(com) || !stop) {
                if (state == AudioState::Initializing) state = AudioState::Failed;
                workerActive = false;
                break;
            }
            session = generation;
            stopEvent = stop.get();
        }

        RunCapture(stop.get(), session);

        const AudioLock lock;
        stopEvent = nullptr;
        if (generation == session) {
            audioSamples.Clear();
            state = AudioState::Failed;
        }
        if (state != AudioState::Initializing) {
            workerActive = false;
            break;
        }
        // Stop then Start may have queued a fresh request while the old client was draining.
    }
    if (SUCCEEDED(com)) CoUninitialize();
    return 0;
}
} // namespace

API int __cdecl NC_DesktopAudioSupported() noexcept {
    using VersionFunction = LONG(WINAPI*)(PRTL_OSVERSIONINFOW);
    const auto symbol = GetProcAddress(GetModuleHandleW(L"ntdll.dll"), "RtlGetVersion");
    if (!symbol) return 0;
    VersionFunction version = nullptr;
    static_assert(sizeof(version) == sizeof(symbol));
    std::memcpy(&version, &symbol, sizeof(version));
    RTL_OSVERSIONINFOW info{};
    info.dwOSVersionInfoSize = sizeof(info);
    return version(&info) == 0 && info.dwMajorVersion >= 10 && info.dwBuildNumber >= 20348;
}

API int __cdecl NC_DesktopAudioStart() noexcept {
    const AudioLock lock;
    if (state == AudioState::Initializing || state == AudioState::Running) return 1;
    if (!NC_DesktopAudioSupported()) {
        state = AudioState::Failed;
        return 0;
    }
    static HMODULE module = nullptr;
    // Windows can still call its completion handler after Stop returns or Unity unloads the plugin.
    if (!module && !GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
                                       reinterpret_cast<LPCWSTR>(&gate), &module)) {
        state = AudioState::Failed;
        return 0;
    }

    state = AudioState::Initializing;
    ++generation;
    audioSamples.Clear();
    if (!workerActive) {
        const uintptr_t worker = _beginthreadex(nullptr, 0, Worker, nullptr, 0, nullptr);
        if (!worker) {
            state = AudioState::Failed;
            return 0;
        }
        workerActive = true;
        CloseHandle(reinterpret_cast<HANDLE>(worker));
    }
    return 1;
}

API int __cdecl NC_DesktopAudioState() noexcept {
    const AudioLock lock;
    return static_cast<int>(state);
}

API int __cdecl NC_DesktopAudioRead(float* output, int capacityFrames) noexcept {
    if (!output || capacityFrames <= 0) return 0;
    const AudioLock lock;
    if (state != AudioState::Running) return 0;
    return static_cast<int>(audioSamples.Read(output, static_cast<uint32_t>(capacityFrames), GetTickCount64()));
}

API void __cdecl NC_DesktopAudioStop() noexcept {
    const AudioLock lock;
    ++generation;
    state = AudioState::Stopped;
    audioSamples.Clear();
    if (stopEvent) SetEvent(stopEvent);
}
