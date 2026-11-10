# Configure one non-secret HTTPS API endpoint in a Windows release, no rebuild.
param([Parameter(Mandatory=$true)][string]$BuildFolder,[Parameter(Mandatory=$true)][string]$Url)
$ErrorActionPreference = 'Stop'
$parsed = $null
if (-not [System.Uri]::TryCreate($Url,[System.UriKind]::Absolute,[ref]$parsed) -or
    $parsed.Scheme -ne 'https' -or $parsed.UserInfo -or $parsed.Query -or
    $parsed.Fragment -or $parsed.AbsolutePath -ne '/' -or
    $parsed.Host -match '(example\.com$|localhost$|^127\.)') {
    throw 'A deployed public HTTPS root URL is required. No credentials or paths are allowed.'
}
$folder=(Resolve-Path -LiteralPath $BuildFolder).Path
if (-not (Test-Path -LiteralPath (Join-Path $folder 'LibraryLounge.exe'))) {
    throw 'BuildFolder must contain LibraryLounge.exe'
}
$target=Join-Path $folder 'LibraryLounge_Data\StreamingAssets'
[System.IO.Directory]::CreateDirectory($target) | Out-Null
[System.IO.File]::WriteAllText((Join-Path $target 'room-service.url'),$parsed.GetLeftPart([System.UriPartial]::Authority)+"`n",[System.Text.UTF8Encoding]::new($false))
Write-Host 'Public HTTPS room address configured in this client folder. No secret written.'
