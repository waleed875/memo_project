<#
.SYNOPSIS
  Uploads the independently packaged Memo Dashboard from this PC to Ubuntu via SSH.
.EXAMPLE
  .\scripts\Deploy-MemoDashboard.ps1 -Server 192.168.1.20 -User deploy -KeyPath C:\Keys\ubuntu_ed25519 -InstallService
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidatePattern('^[a-zA-Z0-9.-]+$')] [string]$Server,
    [Parameter(Mandatory)] [ValidatePattern('^[a-zA-Z0-9_-]+$')] [string]$User,
    [string]$KeyPath,
    [ValidatePattern('^/[a-zA-Z0-9._/-]+$')] [string]$RemoteDirectory = '/opt/memo-dashboard',
    [ValidatePattern('^[a-zA-Z0-9_.@-]+$')] [string]$ServiceName = 'memo-dashboard',
    [switch]$InstallService,
    [string]$PackagePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($PackagePath)) {
    $PackagePath = Get-ChildItem -Path (Join-Path $repositoryRoot 'artifacts') -Filter 'MemoDashboard-*.zip' |
        Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if ([string]::IsNullOrWhiteSpace($PackagePath) -or -not (Test-Path -LiteralPath $PackagePath)) { throw 'لم يتم العثور على حزمة Dashboard ZIP.' }
if (-not [string]::IsNullOrWhiteSpace($KeyPath) -and -not (Test-Path -LiteralPath $KeyPath)) { throw 'ملف مفتاح SSH غير موجود.' }

$sshArguments = @()
if (-not [string]::IsNullOrWhiteSpace($KeyPath)) { $sshArguments += @('-i', $KeyPath) }
$releaseName = 'release-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
$remoteStage = "/tmp/memo-dashboard-$releaseName"
$remoteZip = "$remoteStage/MemoDashboard-$releaseName.zip"
$remoteInstaller = "$remoteStage/install.sh"
$remoteHost = "${User}@${Server}"

$installer = Join-Path $PSScriptRoot 'Install-MemoDashboard.sh'
if (-not (Test-Path -LiteralPath $installer)) { throw 'سكريبت التثبيت الموحّد غير موجود.' }
& ssh @sshArguments $remoteHost "mkdir -p '$remoteStage'"
if ($LASTEXITCODE -ne 0) { throw 'فشل إنشاء مجلد الرفع المؤقت على Ubuntu.' }

Write-Host "رفع الحزمة والمثبّت الموحّد إلى $remoteHost ..." -ForegroundColor Cyan
& scp @sshArguments -- $PackagePath "${remoteHost}:$remoteZip"
if ($LASTEXITCODE -ne 0) { throw 'فشل رفع الحزمة عبر SCP.' }
& scp @sshArguments -- $installer "${remoteHost}:$remoteInstaller"
if ($LASTEXITCODE -ne 0) { throw 'فشل رفع سكربت التثبيت الموحّد.' }
# The package installer owns first install, upgrades, preservation of App_Data,
# runtime installation, health check, and rollback.  Do not manually swap
# /opt symlinks here: that bypasses all of those safeguards.
$remoteCommand = @"
set -e
trap 'rm -rf -- "$remoteStage"' EXIT
sudo bash '$remoteInstaller' --install-root '$RemoteDirectory' --service-name '$ServiceName'
"@
Write-Host 'فك الحزمة وتشغيل مثبّت Dashboard الموحّد...' -ForegroundColor Cyan
& ssh @sshArguments -t $remoteHost $remoteCommand
if ($LASTEXITCODE -ne 0) { throw 'تم رفع الحزمة لكن فشل التثبيت أو التحقق الصحي على Ubuntu.' }
Write-Host "تم نشر الداشبورد بنجاح إلى: $RemoteDirectory/current" -ForegroundColor Green
