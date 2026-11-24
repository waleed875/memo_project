param(
    [int]$Port = 8080,
    [switch]$RunPublished,
    [string]$DataRoot = "",
    [string]$UpdateDataRoot = ""
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot

$env:ASPNETCORE_URLS = "http://0.0.0.0:$Port"
if (-not [string]::IsNullOrWhiteSpace($DataRoot)) { $env:MEMO_DASHBOARD_DATA_DIR = $DataRoot }
if (-not [string]::IsNullOrWhiteSpace($UpdateDataRoot)) { $env:MEMO_UPDATE_DATA_DIR = $UpdateDataRoot }

$publishedExe = Join-Path $Root "publish\MemoDashboard.Backend.exe"
if ($RunPublished -and (Test-Path $publishedExe)) {
    Write-Host "تشغيل النسخة المنشورة من: $publishedExe"
    & $publishedExe
}
else {
    Write-Host "تشغيل نسخة التطوير (dotnet run) على: http://0.0.0.0:$Port"
    & dotnet run --project (Join-Path $Root "src\MemoDashboard.Web\Backend\MemoDashboard.Backend.csproj") -c Release
}
