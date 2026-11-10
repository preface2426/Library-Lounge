#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <roapi.h>
#include <psapi.h>
#include <d3d11.h>
#include <dxgi.h>
#include <windows.graphics.capture.interop.h>
#include <windows.graphics.directx.direct3d11.interop.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Metadata.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>
#include <mutex>
#include <atomic>
#include <memory>
#include <vector>
#include <algorithm>
#include <utility>
#include <chrono>
#include <string>
#include "IUnityInterface.h"
#include "IUnityGraphics.h"
#include "IUnityGraphicsD3D11.h"

using namespace winrt;
using namespace winrt::Windows::Graphics;
using namespace winrt::Windows::Graphics::Capture;
using namespace winrt::Windows::Graphics::DirectX;
using namespace winrt::Windows::Graphics::DirectX::Direct3D11;
using ::Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess;
using Clock = std::chrono::steady_clock;
#define API extern "C" __declspec(dllexport)
extern "C" void __cdecl NC_ControlShutdown();
extern "C" void __cdecl NC_DesktopAudioStop() noexcept;

// All capture/ImmediateContext work runs in Unity's render callback. Main-thread
// calls only change requests or exchange retained COM pointers under this lock.
static std::mutex gate;
static IUnityGraphics* graphics;
static IUnityGraphicsD3D11* unityD3D;
static com_ptr<ID3D11Device> device;
static com_ptr<ID3D11DeviceContext> context;
static com_ptr<ID3D11Texture2D> destination;
static com_ptr<ID3D11Texture2D> pendingDestination;
static bool pendingTextureChange;
static IDirect3DDevice captureDevice{nullptr};
static GraphicsCaptureItem item{nullptr};
static event_token itemClosedToken{};
static std::shared_ptr<std::atomic_bool> itemClosed;
static Direct3D11CaptureFramePool pool{nullptr};
static GraphicsCaptureSession session{nullptr};
static HWND player;
static DWORD originalAffinity;
static bool affinityChanged, requested, restart, capturePaused;
static HMONITOR requestedMonitor;
static Clock::time_point lastCopy;

struct CaptureInfo {
    int state, width, height, generation, error, excluded;
    uint64_t frames;
    double ageMs, submitMs, sourceAgeMs;
};
static_assert(sizeof(CaptureInfo) == 56);
static CaptureInfo info{};
static std::wstring errorText;

struct Monitor { HMONITOR handle; MONITORINFOEXW info; };
static std::vector<Monitor> Monitors() {
    std::vector<Monitor> result;
    EnumDisplayMonitors(nullptr, nullptr, [](HMONITOR monitor, HDC, LPRECT, LPARAM data) -> BOOL {
        Monitor value{monitor, {}}; value.info.cbSize = sizeof(value.info);
        if (GetMonitorInfoW(monitor, &value.info)) reinterpret_cast<std::vector<Monitor>*>(data)->push_back(value);
        return TRUE;
    }, reinterpret_cast<LPARAM>(&result));
    std::stable_sort(result.begin(), result.end(), [](auto& a, auto& b) {
        return (a.info.dwFlags & MONITORINFOF_PRIMARY) > (b.info.dwFlags & MONITORINFOF_PRIMARY);
    });
    return result;
}

static HWND PlayerWindow() {
    HWND found = nullptr;
    EnumWindows([](HWND window, LPARAM data) -> BOOL {
        DWORD pid; GetWindowThreadProcessId(window, &pid);
        wchar_t name[80]{}; GetClassNameW(window, name, 80);
        if (pid == GetCurrentProcessId() && wcscmp(name, L"UnityWndClass") == 0) {
            *reinterpret_cast<HWND*>(data) = window; return FALSE;
        }
        return TRUE;
    }, reinterpret_cast<LPARAM>(&found));
    return found;
}

static void RestoreAffinity() {
    if (affinityChanged && IsWindow(player)) SetWindowDisplayAffinity(player, originalAffinity);
    affinityChanged = false; info.excluded = 0;
}
static void CloseCapture() {
    if (item && itemClosed) item.Closed(itemClosedToken);
    itemClosed.reset();
    if (session) { try { session.Close(); } catch (...) {} session = nullptr; }
    if (pool) { try { pool.Close(); } catch (...) {} pool = nullptr; }
    item = nullptr; captureDevice = nullptr;
    destination = nullptr; pendingDestination = nullptr; pendingTextureChange = false;
    context = nullptr; device = nullptr;
    info.width = info.height = 0; info.frames = 0;
}
static void Fail(hresult code, std::wstring const& message) {
    CloseCapture(); requested = restart = false;
    info.state = 3; info.error = code; errorText = message;
    RestoreAffinity();
}
// Main-thread failures only enqueue shutdown; WinRT session/graphics release belongs to Render.
static void QueueFailure(hresult code, std::wstring const& message) {
    requested = false; restart = true; capturePaused = false;
    info.state = 3; info.error = code; errorText = message;
}

API int __cdecl NC_MonitorCount() { return static_cast<int>(Monitors().size()); }
API int __cdecl NC_MonitorRect(int index, RECT* rect) {
    auto monitors = Monitors();
    if (!rect || index < 0 || index >= static_cast<int>(monitors.size())) return 0;
    *rect = monitors[index].info.rcMonitor; return 1;
}
API int __cdecl NC_MonitorInfo(int index, wchar_t* name, int capacity, int* width, int* height) {
    auto monitors = Monitors();
    if (index < 0 || index >= static_cast<int>(monitors.size())) return 0;
    auto& m = monitors[index].info;
    wcsncpy_s(name, capacity, m.szDevice, _TRUNCATE);
    *width = m.rcMonitor.right - m.rcMonitor.left; *height = m.rcMonitor.bottom - m.rcMonitor.top;
    return 1;
}
API void __cdecl NC_GetInfo(CaptureInfo* value) {
    std::lock_guard lock(gate); *value = info;
    value->ageMs = info.frames ? std::chrono::duration<double, std::milli>(Clock::now() - lastCopy).count() : -1;
}
API void __cdecl NC_GetError(wchar_t* text, int capacity) {
    std::lock_guard lock(gate); wcsncpy_s(text, capacity, errorText.c_str(), _TRUNCATE);
}
API int __cdecl NC_GetMemory(uint64_t* privateBytes,uint64_t* workingBytes) {
    PROCESS_MEMORY_COUNTERS_EX counters{}; counters.cb=sizeof(counters);
    if (!GetProcessMemoryInfo(GetCurrentProcess(),reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&counters),sizeof(counters))) return 0;
    *privateBytes=counters.PrivateUsage; *workingBytes=counters.WorkingSetSize; return 1;
}
API int __cdecl NC_Start(int monitorIndex) {
    std::lock_guard lock(gate);
    auto monitors = Monitors();
    if (monitorIndex < 0 || monitorIndex >= static_cast<int>(monitors.size())) {
        QueueFailure(E_INVALIDARG, L"显示器已移除或编号无效，请刷新列表。"); return 0;
    }
    // WDA_EXCLUDEFROMCAPTURE becomes a black rectangle on older Windows builds.
    using VersionFn = LONG(WINAPI*)(OSVERSIONINFOW*);
    auto versionFn = reinterpret_cast<VersionFn>(GetProcAddress(GetModuleHandleW(L"ntdll.dll"), "RtlGetVersion"));
    OSVERSIONINFOW version{sizeof(version)};
    if (!versionFn || versionFn(&version) != 0 || version.dwBuildNumber < 19041) {
        QueueFailure(E_NOTIMPL, L"A1 需要 Windows 10 2004（19041）或更新版本。"); return 0;
    }
    if (graphics == nullptr || graphics->GetRenderer() != kUnityGfxRendererD3D11 || unityD3D == nullptr) {
        QueueFailure(E_NOTIMPL, L"桌面捕获需要 Direct3D 11 的 Windows 独立运行包。"); return 0;
    }
    if (!affinityChanged) {
        player = PlayerWindow();
        if (!player || !GetWindowDisplayAffinity(player, &originalAffinity) || !SetWindowDisplayAffinity(player, WDA_EXCLUDEFROMCAPTURE)) {
            QueueFailure(HRESULT_FROM_WIN32(GetLastError()), L"无法排除客户端窗口；为避免递归，未开始捕获。"); return 0;
        }
        affinityChanged = true;
    }
    info.excluded = 1; info.state = 1; info.error = 0; errorText.clear();
    DWORD actual = 0;
    if (!GetWindowDisplayAffinity(player, &actual) || actual != WDA_EXCLUDEFROMCAPTURE) {
        QueueFailure(E_FAIL, L"客户端窗口的捕获排除设置未生效。"); return 0;
    }
    capturePaused = false;
    requestedMonitor = monitors[monitorIndex].handle; requested = restart = true;
    return 1;
}
API void __cdecl NC_Stop() {
    std::lock_guard lock(gate); requested = false; restart = true; capturePaused = false;
    info.state = 0; info.error = 0; errorText.clear();
    // Defer destination and session release to the queued render callback.
    // Keep exclusion until the queued render callback closes the capture session.
}
API void __cdecl NC_PauseCapture(int value) {
    std::lock_guard lock(gate);
    bool pause = value != 0;
    if (pause == capturePaused) return;
    capturePaused = pause;
    // Both pause and resume restart/close in the render callback, never on the caller thread.
    restart = true;
}
API int __cdecl NC_SetTexture(void* texture) {
    std::lock_guard lock(gate);
    pendingDestination = nullptr;
    pendingTextureChange = true;
    if (!texture) return 1;
    HRESULT result = static_cast<ID3D11Resource*>(texture)->QueryInterface(pendingDestination.put());
    if (FAILED(result)) { QueueFailure(result, L"无法连接 Unity 的 GPU 纹理。"); return 0; }
    return 1;
}

static void UNITY_INTERFACE_API Render(int) {
    std::lock_guard lock(gate);
    try {
        struct Apartment {
            HRESULT result = RoInitialize(RO_INIT_MULTITHREADED);
            ~Apartment() { if (SUCCEEDED(result)) RoUninitialize(); }
        };
        thread_local Apartment apartment;
        if (FAILED(apartment.result) && apartment.result != RPC_E_CHANGED_MODE) check_hresult(apartment.result);
        if (pendingTextureChange) {
            destination = std::move(pendingDestination);
            pendingTextureChange = false;
        }
        if (restart) {
            CloseCapture(); restart = false;
            if (!requested) { RestoreAffinity(); return; }
            if (capturePaused) return;
            if (!GraphicsCaptureSession::IsSupported()) throw hresult_error(E_NOTIMPL, L"当前 Windows 会话不支持桌面捕获。");
            device.copy_from(unityD3D->GetDevice());
            device->GetImmediateContext(context.put());
            auto dxgi = device.as<IDXGIDevice>();
            com_ptr<IInspectable> inspectable;
            check_hresult(CreateDirect3D11DeviceFromDXGIDevice(dxgi.get(), inspectable.put()));
            captureDevice = inspectable.as<IDirect3DDevice>();
            auto factory = get_activation_factory<GraphicsCaptureItem, IGraphicsCaptureItemInterop>();
            HRESULT monitorAccess = factory->CreateForMonitor(requestedMonitor, guid_of<GraphicsCaptureItem>(), put_abi(item));
            if (FAILED(monitorAccess))
                throw hresult_error(monitorAccess,
                    L"Windows.Graphics.Capture::CreateForMonitor 无法创建显示器捕获对象（系统权限、显示器会话或图形驱动限制）。");
            // The OS callback only marks its own session; it never waits on the render lock.
            auto closed = std::make_shared<std::atomic_bool>(false);
            itemClosedToken = item.Closed([closed](GraphicsCaptureItem const&, winrt::Windows::Foundation::IInspectable const&) {
                closed->store(true, std::memory_order_relaxed);
            });
            itemClosed = std::move(closed);
            auto size = item.Size();
            if (size.Width <= 0 || size.Height <= 0) throw hresult_error(E_FAIL, L"显示器尺寸不可用。");
            pool = Direct3D11CaptureFramePool::CreateFreeThreaded(captureDevice, DirectXPixelFormat::B8G8R8A8UIntNormalized, 2, size);
            session = pool.CreateCaptureSession(item);
            session.IsCursorCaptureEnabled(false);
            // Recent Windows versions otherwise retain a roughly 60 Hz capture ceiling.
            if (winrt::Windows::Foundation::Metadata::ApiInformation::IsPropertyPresent(
                L"Windows.Graphics.Capture.GraphicsCaptureSession", L"MinUpdateInterval")) {
                MONITORINFOEXW monitor{}; monitor.cbSize = sizeof(monitor);
                DEVMODEW mode{}; mode.dmSize = sizeof(mode);
                int hz = 60;
                if (GetMonitorInfoW(requestedMonitor, &monitor) && EnumDisplaySettingsExW(monitor.szDevice, ENUM_CURRENT_SETTINGS, &mode, 0) && mode.dmDisplayFrequency > 1)
                    hz = static_cast<int>(mode.dmDisplayFrequency);
                // One Hz of headroom avoids rounding fractional display timings down to half rate.
                session.MinUpdateInterval(std::chrono::microseconds(1000000 / (hz + 1)));
            }
            session.StartCapture();
            info.width = size.Width; info.height = size.Height; info.generation++;
            info.state = 2;
        }
        if (capturePaused || !requested || !pool) return;
        if (itemClosed && itemClosed->load(std::memory_order_relaxed)) {
            Fail(E_ABORT, L"共享的显示器已断开，请重新选择显示器。");
            return;
        }
        // One nonblocking poll per rendered frame; VSync paces Unity, no second timer or 60 Hz ceiling.
        auto frame = pool.TryGetNextFrame();
        if (!frame) return;
        // Drain queued old frames to display the newest available desktop.
        for (int i = 0; i < 2; i++) {
            auto next = pool.TryGetNextFrame();
            if (!next) break;
            frame.Close(); frame = next;
        }
        auto size = frame.ContentSize();
        if (size.Width <= 0 || size.Height <= 0) { frame.Close(); return; }
        if (size.Width != info.width || size.Height != info.height) {
            frame.Close(); destination = nullptr;
            pool.Recreate(captureDevice, DirectXPixelFormat::B8G8R8A8UIntNormalized, 2, size);
            info.width = size.Width; info.height = size.Height; info.generation++; info.frames = 0;
            return;
        }
        if (destination) {
            D3D11_TEXTURE2D_DESC target{}; destination->GetDesc(&target);
            auto access = frame.Surface().as<IDirect3DDxgiInterfaceAccess>();
            com_ptr<ID3D11Texture2D> source;
            check_hresult(access->GetInterface(guid_of<ID3D11Texture2D>(), source.put_void()));
            D3D11_TEXTURE2D_DESC sourceDesc{}; source->GetDesc(&sourceDesc);
            if (target.Width == static_cast<UINT>(info.width) && target.Height == static_cast<UINT>(info.height) &&
                sourceDesc.Width == target.Width && sourceDesc.Height == target.Height) {
                auto before = Clock::now();
                context->CopyResource(destination.get(), source.get());
                info.submitMs = std::chrono::duration<double, std::milli>(Clock::now() - before).count();
                info.frames++; lastCopy = Clock::now();
                LARGE_INTEGER counter,frequency; QueryPerformanceCounter(&counter); QueryPerformanceFrequency(&frequency);
                info.sourceAgeMs = counter.QuadPart * 1000.0 / frequency.QuadPart - frame.SystemRelativeTime().count() / 10000.0;
            }
        }
        frame.Close();
    } catch (hresult_error const& e) { Fail(e.code(), e.message().c_str()); }
      catch (...) { Fail(E_FAIL, L"桌面捕获失败，请停止后重试。"); }
}
API UnityRenderingEvent __cdecl NC_RenderEvent() { return Render; }

static void UNITY_INTERFACE_API DeviceEvent(UnityGfxDeviceEventType event) {
    if (event == kUnityGfxDeviceEventShutdown) {
        std::lock_guard lock(gate); requested = restart = false; CloseCapture(); RestoreAffinity(); info.state = 0;
    }
}
extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* interfaces) {
    graphics = interfaces->Get<IUnityGraphics>(); unityD3D = interfaces->Get<IUnityGraphicsD3D11>();
    graphics->RegisterDeviceEventCallback(DeviceEvent);
}
extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginUnload() {
    NC_DesktopAudioStop();
    NC_ControlShutdown(); // Restore hooks, cursor and raw input even on plugin unload.
    DeviceEvent(kUnityGfxDeviceEventShutdown);
    if (graphics) graphics->UnregisterDeviceEventCallback(DeviceEvent);
    graphics = nullptr; unityD3D = nullptr;
}
