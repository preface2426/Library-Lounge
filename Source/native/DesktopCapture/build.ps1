param([string]$UnityEditor = 'C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor')
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vsRoot = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vsRoot) {
    $vsRoot = Get-ChildItem "$env:ProgramFiles\Microsoft Visual Studio\*\*\VC\Auxiliary\Build\vcvars64.bat" -ErrorAction SilentlyContinue |
        Select-Object -First 1 | ForEach-Object { [IO.Path]::GetFullPath((Join-Path $_.DirectoryName '..\..\..')) }
}
if (!$vsRoot) { throw '需要 Visual Studio C++ 桌面开发工具。' }
$vcvars = Join-Path $vsRoot 'VC\Auxiliary\Build\vcvars64.bat'
$pluginApi = Join-Path $UnityEditor 'Data\PluginAPI'
$pluginFolder = Join-Path $projectRoot 'NetCafeClient\Assets\Plugins\x86_64'
$intermediate = Join-Path $projectRoot 'Builds\Native'
New-Item -ItemType Directory -Force -Path $pluginFolder,$intermediate | Out-Null
$dll = Join-Path $pluginFolder 'NetCafeCapture.dll'
# cmd initializes the compiler environment only; file operations stay in PowerShell.
$compile = 'call "{0}" >nul && cl /nologo /std:c++20 /utf-8 /EHsc /O2 /MT /W4 /LD /DUNICODE /D_UNICODE /I"{1}" "{2}" "{3}" "{6}" "{7}" /Fo"{4}\\" /link /OUT:"{5}" /IMPLIB:"{4}\NetCafeCapture.lib" d3d11.lib dxgi.lib windowsapp.lib user32.lib gdi32.lib runtimeobject.lib psapi.lib Magnification.lib mmdevapi.lib ole32.lib' -f $vcvars,$pluginApi,(Join-Path $PSScriptRoot 'DesktopCapture.cpp'),(Join-Path $PSScriptRoot 'A1TestPattern.cpp'),$intermediate,$dll,(Join-Path $PSScriptRoot 'DesktopControl.cpp'),(Join-Path $PSScriptRoot 'DesktopAudio.cpp')
& $env:ComSpec /d /s /c $compile
if ($LASTEXITCODE -ne 0) { throw "原生模块编译失败：$LASTEXITCODE" }
Write-Output "NATIVE_BUILD_PASS: $dll"
