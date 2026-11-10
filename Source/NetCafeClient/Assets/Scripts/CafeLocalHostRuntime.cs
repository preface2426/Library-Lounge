using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using UnityEngine.Networking;

// Opt-in developer-host adapter: started only by the local Create Room page.
// A distributed client has no room-service source/LiveKit executable, so it
// cannot launch or own any infrastructure. Never run with public credentials.
public sealed class CafeLocalHostRuntime : MonoBehaviour
{
    Process livekit;
    Process roomApi;
    string projectRoot;
    int port;
    int livekitPort;
    public bool Starting { get; private set; }
    public bool Ready { get; private set; }
    public string LocalUrl => Ready ? "http://127.0.0.1:" + port : "";
    int epoch;
    CafeLoopbackHttp healthRequest;

    public static bool IsLoopbackUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var u) &&
               u.Scheme == Uri.UriSchemeHttp && u.IsLoopback &&
               (u.Host == "127.0.0.1" || u.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase));
    }

    static string FindHostRoot()
    {
        if (Application.platform != RuntimePlatform.WindowsPlayer) return null;
        DirectoryInfo parent = new DirectoryInfo(Application.dataPath);
        for (int i = 0; parent != null && i < 5; i++, parent = parent.Parent)
        {
            string root = parent.FullName;
            bool bundled = File.Exists(Path.Combine(root, "room-service", "room-service.mjs"));
            bool source = File.Exists(Path.Combine(root, "room-service", "src", "server.ts")) && File.Exists(Path.Combine(root, "room-service", "node_modules", "tsx", "dist", "cli.mjs"));
            if ((bundled || source) &&
                File.Exists(Path.Combine(root, "Tools", "LiveKitLocalTest", "v1.13.11", "livekit-server.exe")))
                return root;
        }
        return null;
    }

    static string FindNode(string root)
    {
        string bundled = root == null ? "" : Path.Combine(root, "Tools", "Node", "node.exe");
        return File.Exists(bundled) ? bundled : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe");
    }
    public bool HostToolsAvailable => FindHostRoot() != null && File.Exists(FindNode(FindHostRoot()));

    static bool PortFree(int candidate)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, candidate);
            listener.Start(); listener.Stop();
            return true;
        }
        catch (SocketException) { return false; }
    }

    static bool Alive(Process child)
    {
        try { return child != null && !child.HasExited; }
        catch (InvalidOperationException) { return false; }
    }

    static void StopOwned(ref Process child)
    {
        if (child == null) return;
        try { if (!child.HasExited) child.Kill(); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        finally { child.Dispose(); child = null; }
    }

    public IEnumerator EnsureReady(Action<string, string> done)
    {
        if (Ready && Alive(livekit) && Alive(roomApi))
        {
            done(LocalUrl, null); yield break;
        }
        if (Starting) { done(null, "本机房间服务仍在启动，请稍候。"); yield break; }
        StopOwned();
        Starting = true;
        int request = ++epoch;
        string error = null;
        projectRoot = FindHostRoot();
        string node = FindNode(projectRoot);
        if (projectRoot == null || !File.Exists(node))
            error = "本机房主组件未找到。可填写已部署的 HTTPS 房间地址；朋友端无需安装房主组件。";
        // Never take over an existing developer's 7880 process. Use a private
        // reserved loopback range so other test sessions remain untouched.
        livekitPort = 0;
        if (error == null)
        {
            for (int p = 7980; p <= 7990; p++)
                if (PortFree(p) && PortFree(p + 100)) { livekitPort = p; break; }
            if (livekitPort == 0) error = "本机 LiveKit 测试端口 7980～7990 均被占用。";
        }
        port = 0;
        if (error == null)
        {
            for (int p = 8787; p <= 8807; p++)
                if (PortFree(p)) { port = p; break; }
            if (port == 0) error = "本地房间端口 8787～8807 均已占用。";
        }
        if (error == null)
        {
            try
            {
                var info = new ProcessStartInfo(Path.Combine(projectRoot, "Tools", "LiveKitLocalTest", "v1.13.11", "livekit-server.exe"))
                {
                    Arguments = "--dev --bind 127.0.0.1 --node-ip 127.0.0.1 --port " + livekitPort +
                        " --rtc.tcp_port " + (livekitPort + 100) + " --udp-port " + (livekitPort + 200),
                    WorkingDirectory = projectRoot,
                    UseShellExecute = false, CreateNoWindow = true
                };
                livekit = Process.Start(info);
            }
            catch (Exception e) { error = "LiveKit 本地启动失败：" + e.GetType().Name; }
        }
        if (error == null)
        {
            float until = Time.realtimeSinceStartup + 12f;
            while (request == epoch && Time.realtimeSinceStartup < until && PortFree(livekitPort) && Alive(livekit))
                yield return null;
            if (request != epoch) yield break;
            if (PortFree(livekitPort) || !Alive(livekit)) error = "LiveKit 未能监听本地端口。";
        }
        if (error == null)
        {
            try
            {
                var info = new ProcessStartInfo(node)
                {
                    Arguments = File.Exists(Path.Combine(projectRoot,"room-service","room-service.mjs")) ? "room-service.mjs" : "--import tsx src/server.ts",
                    WorkingDirectory = Path.Combine(projectRoot, "room-service"),
                    UseShellExecute = false, CreateNoWindow = true
                };
                // These test-only keys are valid solely for loopback --dev.
                info.EnvironmentVariables["HOST"] = "127.0.0.1";
                info.EnvironmentVariables["PORT"] = port.ToString();
                info.EnvironmentVariables["LIVEKIT_URL"] = "http://127.0.0.1:" + livekitPort;
                info.EnvironmentVariables["LIVEKIT_WS_URL"] = "ws://127.0.0.1:" + livekitPort;
                info.EnvironmentVariables["LIVEKIT_API_KEY"] = "devkey";
                info.EnvironmentVariables["LIVEKIT_API_SECRET"] = "secret";
                roomApi = Process.Start(info);
            }
            catch (Exception e) { error = "房间服务启动失败：" + e.GetType().Name; }
        }
        if (error == null)
        {
            float until = Time.realtimeSinceStartup + 18f;
            string healthError="";
            while (request == epoch && Time.realtimeSinceStartup < until && Alive(roomApi) && Alive(livekit))
            {
                using (var req = new CafeLoopbackHttp("http://127.0.0.1:" + port + "/readyz","GET",timeoutSeconds:2))
                {
                    healthRequest=req;
                    try
                    {
                    while(request==epoch&&!req.Pending.IsCompleted)yield return null;
                    if(request!=epoch)yield break;
                    var result=req.Pending.GetAwaiter().GetResult();
                    healthError=result.Status>0?"HTTP "+result.Status:result.Error;
                    if (result.Success && result.Status == 200)
                    {
                        Ready = true; break;
                    }
                    }
                    finally { if(ReferenceEquals(healthRequest,req))healthRequest=null; }
                }
                if (!Ready) yield return new WaitForSecondsRealtime(.2f);
            }
            if (request != epoch) yield break;
            if (!Ready) error = "房间服务健康检查失败（"+healthError+"）。";
        }
        Starting = false;
        if (error != null) { StopOwned(); done(null, error); }
        else done(LocalUrl, null);
    }

    public void StopOwned()
    {
        ++epoch;
        healthRequest?.Dispose();healthRequest=null;
        Starting = false; Ready = false;
        // Only processes started by THIS game instance may be stopped.
        StopOwned(ref roomApi);
        StopOwned(ref livekit);
        port = 0;
        livekitPort = 0;
    }

    void OnDestroy() { StopOwned(); }
    void OnApplicationQuit() { StopOwned(); }
}
