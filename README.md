# Library Lounge · 线上网吧

当前源码快照：**v0.1.3 Restroom R11 · Player Recovery**（2026-11-10，Windows x64 测试候选）。包含 Unity 客户端、原生桌面采集与输入模块、房间服务，以及公共大屏和角色回位的当前实现。

本版第一人称视角跟随所选角色的眼睛高度。掉出地板会回到房内起点，也可从「Esc → 更多 → 返回初始位置」手动回位。已开启的桌面分享与大屏投放在离座或回位后继续。上一个 R11 版本加入的桌面声音、最高 2560×1440 共享和分阶段开房超时提示也包含在本快照中。

## 打开 Unity 工程

1. 安装 Unity **6000.6.7f1**（包含 Windows Build Support）。
2. 从[原作者页面](https://reodesuxz.itch.io/low-poly-furniture-pack-vol-2)下载 `LowPolyFurniturePackVol2_v1.0.zip`。仓库保留 30 个模型的 Unity 导入元数据和文件校验值；作者不允许公开分发模型文件。
3. 安装 Python 3.10 或更新版本，在仓库根目录运行 `python Source/scripts/restore-furniture.py <下载的ZIP路径>`。带空格的路径请加引号。
4. 用 Unity Hub 打开 `Source/NetCafeClient`。主场景为 `Assets/ArtV3/Scenes/Restroom_Playable_v013.unity`。LiveKit Unity SDK 2.1.1 的工程副本已随仓库保留。

Windows 构建可通过 Unity 的 Build Profiles 进行。也可从仓库根目录在 PowerShell 运行：

```powershell
& "<Unity.exe 的完整路径>" -batchmode -quit -projectPath "$PWD/Source/NetCafeClient" -executeMethod LibraryLoungeStationBuild.BuildMouse -mouseBuild=PlayerRecovery2 -logFile "$PWD/unity-build.log"
```

独立客户端生成在 `Builds/`。需要重编译桌面采集插件时，使用 `Source/native/DesktopCapture/build.ps1`，需要 Visual Studio C++ 桌面工具链。

## 房间服务

需要 Node.js 22 或更新版本。进入 `Source/room-service`，运行 `npm ci`、`npm run typecheck` 和 `npm test`。

本机联调需要 [LiveKit Server v1.13.11 for Windows x64](https://github.com/livekit/livekit/releases/tag/v1.13.11)。校验值在 `Source/Tools/LiveKitLocalTest/v1.13.11/checksums.txt`。从下载的包中解压并运行 `livekit-server.exe --dev --bind 127.0.0.1`。在另一个 PowerShell 窗口进入 `Source/room-service`：

```powershell
$env:HOST = "127.0.0.1"
$env:PORT = "8787"
$env:LIVEKIT_URL = "http://127.0.0.1:7880"
$env:LIVEKIT_WS_URL = "ws://127.0.0.1:7880"
$env:LIVEKIT_API_KEY = "devkey"
$env:LIVEKIT_API_SECRET = "secret"
npm start
```

以上是 LiveKit 自带的本机开发值。跨网络联调需要 HTTPS 房间服务和独立的 LiveKit 配置，正式密钥只在服务端设置。Render 配置参考见 `Source/deploy/render.example.yaml`。

本快照是测试候选；公网在朋友网络上偶发的开房超时和真实跨设备音视频仍待验。仓库包含开发源码，Windows 分享包和历史冻结版本独立保留。
