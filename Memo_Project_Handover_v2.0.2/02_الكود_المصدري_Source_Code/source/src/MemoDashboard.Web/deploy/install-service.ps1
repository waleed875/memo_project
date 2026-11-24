param(
    [string]$ServiceName = "MemoDashboard",
    [string]$ServiceDisplayName = "Memo Live Dashboard",
    [string]$ServiceDescription = "لوحة متابعة مذكرات التدقيق الداخلية (ويب للعرض على شاشة LAN)",
    [string]$PublishDir = "",
    [string]$DataRoot = "C:\ProgramData\AuditMemoSystem\MemoDashboard",
    [string]$UpdateDataRoot = "C:\ProgramData\AuditMemoSystem\MemoDashboard\updates"
)

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $DataRoot, $UpdateDataRoot | Out-Null
$Root = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($PublishDir)) {
    $PublishDir = Join-Path $Root "publish"
}

$exePath = Join-Path $PublishDir "MemoDashboard.Backend.exe"
if (-not (Test-Path $exePath)) {
    throw "لم يتم العثور على: $exePath`nقم بتشغيل scripts\publish.ps1 أولاً."
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "الخدمة موجودة مسبقاً، يتم إيقافها وحذفها أولاً..."
    & sc.exe stop $ServiceName | Out-Null
    Start-Sleep -Seconds 2
    & sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

$quoted = "`"$exePath`""
& sc.exe create $ServiceName "binPath=" $quoted "start=" "auto" "DisplayName=" $ServiceDisplayName | Out-Null
& sc.exe description $ServiceName $ServiceDescription | Out-Null
$serviceKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
New-ItemProperty -Path $serviceKey -Name Environment -PropertyType MultiString -Value @("MEMO_DASHBOARD_DATA_DIR=$DataRoot", "MEMO_UPDATE_DATA_DIR=$UpdateDataRoot") -Force | Out-Null
& sc.exe failure $ServiceName "reset=" 86400 "actions=" "restart/5000/restart/30000/restart/60000" | Out-Null
& sc.exe start $ServiceName | Out-Null

Start-Sleep -Seconds 3
$service = Get-Service -Name $ServiceName
Write-Host "حالة الخدمة: $($service.Status)"
if ($service.Status -eq 'Running') {
    Write-Host "تم تثبيت الخدمة وتشغيلها بنجاح. يمكن الوصول للوحة عبر: http://<عنوان-الجهاز>:8080"
}
else {
    Write-Host "تم إنشاء الخدمة لكنها لم تبدأ بعد، راجع سجل الأحداث (Event Viewer)."
}
