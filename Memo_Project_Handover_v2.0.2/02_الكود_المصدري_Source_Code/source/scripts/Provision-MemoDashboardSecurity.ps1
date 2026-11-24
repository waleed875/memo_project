<#
.SYNOPSIS
  Generates the MemoDashboard first-run token and update-service key files once.

.DESCRIPTION
  Run this command on the server, outside the published release directory.
  Existing files are preserved. The first-run token is printed only when it is
  created (or when -RegenerateSetupToken is explicitly supplied).
#>
[CmdletBinding()]
param(
    [string]$SecretDirectory = (Join-Path $env:ProgramData 'AuditMemoSystem\MemoDashboard\secrets'),
    [string]$ServiceAccount = 'NT SERVICE\MemoDashboard',
    [switch]$EnableCheckIn,
    [switch]$RegenerateSetupToken,
    [switch]$CopySetupToken
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-OutsideReleaseRoot {
    param([string]$Candidate)
    $resolved = [IO.Path]::GetFullPath($Candidate).TrimEnd('\')
    $releaseRoot = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
    $relative = [IO.Path]::GetRelativePath($releaseRoot, $resolved)
    if ($relative -eq '.' -or (-not [IO.Path]::IsPathRooted($relative) -and
        $relative -ne '..' -and -not $relative.StartsWith('..' + [IO.Path]::DirectorySeparatorChar) -and
        -not $relative.StartsWith('..' + [IO.Path]::AltDirectorySeparatorChar))) {
        throw 'لا يجوز حفظ الأسرار داخل مجلد السكربت أو مجلد الإصدار المنشور.'
    }
}

function New-RandomSecret {
    param([int]$Bytes = 48)
    $buffer = [byte[]]::new($Bytes)
    [Security.Cryptography.RandomNumberGenerator]::Fill($buffer)
    return [Convert]::ToHexString($buffer).ToLowerInvariant()
}

function Resolve-SecretIdentity {
    param([string]$Name)
    if ($Name.StartsWith('*')) {
        return [System.Security.Principal.SecurityIdentifier]::new($Name.Substring(1))
    }
    return [System.Security.Principal.NTAccount]::new($Name)
}

function New-SecretAccessRule {
    param(
        [string]$Identity,
        [string]$Rights,
        [switch]$Directory
    )
    $identityReference = Resolve-SecretIdentity $Identity
    $rightsValue = [System.Security.AccessControl.FileSystemRights]::$Rights
    if ($Directory) {
        return [System.Security.AccessControl.FileSystemAccessRule]::new(
            $identityReference,
            $rightsValue,
            [System.Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [System.Security.AccessControl.InheritanceFlags]::ObjectInherit,
            [System.Security.AccessControl.PropagationFlags]::None,
            [System.Security.AccessControl.AccessControlType]::Allow)
    }
    return [System.Security.AccessControl.FileSystemAccessRule]::new(
        $identityReference,
        $rightsValue,
        [System.Security.AccessControl.AccessControlType]::Allow)
}

function Set-SecretAcl {
    param([string]$Path)
    $acl = Get-Acl -LiteralPath $Path
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($entry in @(
        @('*S-1-5-18', 'FullControl'),
        @('*S-1-5-32-544', 'FullControl'),
        @($ServiceAccount, 'Read')
    )) {
        try {
            $acl.SetAccessRule((New-SecretAccessRule $entry[0] $entry[1]))
        }
        catch {
            throw "تعذر منح حساب الخدمة صلاحية قراءة ملف السر بأمان. تحقق من -ServiceAccount ثم أعد المحاولة."
        }
    }
    Set-Acl -LiteralPath $Path -AclObject $acl
}

function Set-SecretDirectoryAcl {
    param([string]$Path)
    $acl = Get-Acl -LiteralPath $Path
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($entry in @(
        @('*S-1-5-18', 'FullControl'),
        @('*S-1-5-32-544', 'FullControl'),
        @($ServiceAccount, 'ReadAndExecute')
    )) {
        try {
            $acl.SetAccessRule((New-SecretAccessRule $entry[0] $entry[1] -Directory))
        }
        catch {
            throw 'تعذر ضبط صلاحيات مجلد الأسرار. صحح اسم حساب الخدمة ثم أعد المحاولة؛ لم تُطبع أي قيمة سرية.'
        }
    }
    Set-Acl -LiteralPath $Path -AclObject $acl
}

function Write-NewSecretFile {
    param(
        [string]$Path,
        [string]$Value
    )
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($Value + [Environment]::NewLine)
    try {
        $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try {
            $stream.Write($bytes, 0, $bytes.Length)
            $stream.Flush($true)
        }
        finally {
            $stream.Dispose()
        }
    }
    catch [IO.IOException] {
        throw "تعذر إنشاء ملف سر جديد: $Path"
    }
}

function Ensure-TextSecret {
    param(
        [string]$Path,
        [scriptblock]$Factory,
        [bool]$Regenerate = $false
    )
    if (Test-Path -LiteralPath $Path -PathType Leaf) {
        if ($Regenerate) {
            throw "لن يُستبدل السر الموجود تلقائياً: $Path. احذف الملف يدوياً فقط بعد إجراء تدوير موثق."
        }
        return $false
    }
    Write-NewSecretFile -Path $Path -Value (& $Factory)
    return $true
}

if ([string]::IsNullOrWhiteSpace($env:ProgramData)) {
    throw 'متغير ProgramData غير متوفر؛ مرر SecretDirectory مطلقاً.'
}
if (-not [IO.Path]::IsPathRooted($SecretDirectory)) {
    throw 'SecretDirectory يجب أن يكون مساراً مطلقاً خارج مجلد الإصدار.'
}
Test-OutsideReleaseRoot $SecretDirectory
[IO.Directory]::CreateDirectory($SecretDirectory) | Out-Null


$setupTokenPath = Join-Path $SecretDirectory 'setup-token'
$privateKeyPath = Join-Path $SecretDirectory 'signing-private-key.pem'
$publicKeyPath = Join-Path $SecretDirectory 'signing-public-key.pem'
$adminKeyPath = Join-Path $SecretDirectory 'legacy-admin-key'
$checkInKeyPath = Join-Path $SecretDirectory 'checkin-device-key'

$tokenCreated = $false
if ($RegenerateSetupToken) {
    if (Test-Path -LiteralPath $setupTokenPath) {
        Remove-Item -LiteralPath $setupTokenPath -Force
    }
    $tokenCreated = Ensure-TextSecret $setupTokenPath { New-RandomSecret } $false
}
else {
    $tokenCreated = Ensure-TextSecret $setupTokenPath { New-RandomSecret } $false
}

$privateExists = Test-Path -LiteralPath $privateKeyPath -PathType Leaf
$publicExists = Test-Path -LiteralPath $publicKeyPath -PathType Leaf
if ($privateExists -xor $publicExists) {
    throw 'زوج RSA غير مكتمل. لم يُنشأ مفتاح بديل؛ أصلح private/public pair يدوياً وفق إجراء تدوير موثق.'
}
if (-not $privateExists) {
    $rsa = [Security.Cryptography.RSA]::Create(3072)
    try {
        Write-NewSecretFile -Path $privateKeyPath -Value $rsa.ExportRSAPrivateKeyPem()
        Write-NewSecretFile -Path $publicKeyPath -Value $rsa.ExportSubjectPublicKeyInfoPem()
    }
    finally {
        $rsa.Dispose()
    }
}
else {
    Set-SecretAcl $privateKeyPath
    Set-SecretAcl $publicKeyPath
}

[void](Ensure-TextSecret $adminKeyPath { New-RandomSecret })
if ($EnableCheckIn) {
    [void](Ensure-TextSecret $checkInKeyPath { New-RandomSecret })
}


Write-Host ''
Write-Host 'اكتملت تهيئة ملفات أمان MemoDashboard خارج مجلد الإصدار.' -ForegroundColor Green
Write-Host "مسار الأسرار: $SecretDirectory"
Write-Host 'اربط متغيرات *_FILE التالية بحساب الخدمة (لا تنسخ القيم نفسها إلى appsettings أو ZIP):'
Write-Host "MEMO_DASHBOARD_SETUP_TOKEN_FILE=$setupTokenPath"
Write-Host "MEMO_UPDATE_SIGNING_PRIVATE_KEY_FILE=$privateKeyPath"
Write-Host "MEMO_UPDATE_SIGNING_PUBLIC_KEY_FILE=$publicKeyPath"
Write-Host "MEMO_UPDATE_ADMIN_KEY_FILE=$adminKeyPath"
if ($EnableCheckIn) { Write-Host "MEMO_UPDATE_CHECKIN_KEY_FILE=$checkInKeyPath" }
if ($tokenCreated) {
    $setupToken = (Get-Content -LiteralPath $setupTokenPath -Raw).Trim()
    
Write-Host ''
    Write-Host 'رمز التهيئة الأولى — انسخه الآن؛ لن يُعرض مرة أخرى من هذا الأمر:' -ForegroundColor Yellow
    Write-Host $setupToken -ForegroundColor Yellow
    if ($CopySetupToken -and (Get-Command Set-Clipboard -ErrorAction SilentlyContinue)) {
        Set-Clipboard -Value $setupToken
        Write-Host 'نُسخ الرمز إلى الحافظة.' -ForegroundColor Yellow
    }
}
else {
    Write-Host 'لم يُعرض رمز التهيئة: الملف موجود مسبقاً ولم يُستبدل.'
}
$secretFiles = @($setupTokenPath, $privateKeyPath, $publicKeyPath, $adminKeyPath)
if ($EnableCheckIn) { $secretFiles += $checkInKeyPath }
foreach ($secretFile in $secretFiles) {
    if (Test-Path -LiteralPath $secretFile -PathType Leaf) {
        Set-SecretAcl $secretFile
    }
}
Set-SecretDirectoryAcl $SecretDirectory
Write-Host 'لا يعرض هذا الأمر المفتاح الخاص أو legacy admin key أو check-in key.'