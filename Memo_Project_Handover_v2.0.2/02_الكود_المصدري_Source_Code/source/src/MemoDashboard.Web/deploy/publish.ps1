param(
    [string]$Runtime = "win-x64",
    [switch]$SelfContained,
    [string]$Output = ""
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path $Root "publish"
}

Write-Host "==> [1/3] بناء الواجهة الأمامية (React/Vite)..."

$FrontendDir = Join-Path $Root "Frontend"
Push-Location $FrontendDir
try {
    if (-not (Test-Path (Join-Path $FrontendDir "node_modules"))) {
        & npm.cmd ci
        if ($LASTEXITCODE -ne 0) { throw "فشل تثبيت الحزم (npm ci)" }
    }
    & npm.cmd run build
    if ($LASTEXITCODE -ne 0) { throw "فشل بناء الواجهة الأمامية (npm run build)" }
}
finally {
    Pop-Location
}

Write-Host "==> [2/3] نشر الخادم الخلفي (ASP.NET Core)..."

$selfContainedFlag = "true"
if (-not $SelfContained) { $selfContainedFlag = "false" }

& dotnet publish (Join-Path $Root "Backend\MemoDashboard.Backend.csproj") `
    -c Release -r $Runtime --self-contained $selfContainedFlag `
    -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true `
    -o $Output
if ($LASTEXITCODE -ne 0) { throw "فشل نشر الخادم الخلفي (dotnet publish)" }

Write-Host "==> [3/3] تم النشر بنجاح."
Write-Host "الملفات الناتجة في: $Output"
Write-Host "قم بتشغيل: $Output\MemoDashboard.Backend.exe (أو عبر scripts\run-dashboard.ps1)"
