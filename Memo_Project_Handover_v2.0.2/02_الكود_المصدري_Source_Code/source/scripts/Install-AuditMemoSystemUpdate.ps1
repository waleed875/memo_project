[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$PackagePath,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$InstallDirectory,
    [ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$ExpectedSha256,
    [ValidateRange(1, 1024)][int]$MaximumPackageSizeMiB = 1024,
    [ValidateRange(1, 102400)][int]$MaximumArchiveEntries = 20000,
    [ValidateRange(1, 102400)][int]$MaximumExtractedSizeMiB = 4096,
    [ValidateRange(1, 10000)][int]$MaximumCompressionRatio = 200
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-UpdateLog {
    param([string]$Message, [string]$Level = 'INFO')
    $line = "{0:O} [{1}] {2}" -f (Get-Date), $Level, $Message
    Write-Host $line
    Add-Content -LiteralPath $script:LogPath -Value $line -Encoding utf8
}

function Get-SidecarHash {
    param([string]$Path)
    $sidecar = "$Path.sha256"
    if (-not (Test-Path -LiteralPath $sidecar -PathType Leaf)) { return $null }
    $match = [regex]::Match((Get-Content -LiteralPath $sidecar -Raw), '(?im)\b[a-f0-9]{64}\b')
    if (-not $match.Success) { throw "ملف SHA-256 الجانبي لا يحتوي تجزئة صالحة: $sidecar" }
    $match.Value.ToUpperInvariant()
}

function Get-RelativeStoragePaths {
    param([string]$ConfigPath)
    $paths = @('Data')
    if (-not (Test-Path -LiteralPath $ConfigPath -PathType Leaf)) { return $paths }
    try { $config = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json } catch { throw "تعذر قراءة appsettings.json الحالي: $($_.Exception.Message)" }
    if ($null -ne $config.Storage) {
        foreach ($name in 'ImagesPath', 'BackupsPath', 'ExportsPath') {
            $property = $config.Storage.PSObject.Properties[$name]
            if ($null -ne $property -and $property.Value -is [string] -and -not [string]::IsNullOrWhiteSpace($property.Value)) { $paths += $property.Value }
        }
    }
    $safe = foreach ($path in $paths) {
        $normalized = $path.Replace('/', '\').Trim()
        if ([IO.Path]::IsPathRooted($normalized) -or $normalized -match '(^|\\)\.\.?(\\|$)' -or $normalized -match ':') {
            Write-UpdateLog "لن يُحفظ مسار تخزين غير نسبي أو غير آمن: $path" 'WARN'
            continue
        }
        $normalized.TrimEnd('\')
    }
    @($safe | Where-Object { $_ } | Select-Object -Unique)
}

function Copy-PreservedItemSafe {
    param([string]$Source, [string]$Destination)
    $item = Get-Item -LiteralPath $Source -Force -ErrorAction Stop
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "تم رفض reparse point ضمن البيانات المحلية: $Source"
    }
    if ($item.PSIsContainer) {
        [IO.Directory]::CreateDirectory($Destination) | Out-Null
        foreach ($child in Get-ChildItem -LiteralPath $Source -Force) {
            Copy-PreservedItemSafe -Source $child.FullName -Destination (Join-Path $Destination $child.Name)
        }
        return
    }
    [IO.Directory]::CreateDirectory((Split-Path -Parent $Destination)) | Out-Null
    [IO.File]::Copy($item.FullName, $Destination, $true)
}

function Copy-PackageSnapshot {
    param([string]$Source, [string]$Destination, [Int64]$MaximumBytes)
    $input = [IO.File]::Open($Source, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        if ($input.Length -gt $MaximumBytes) { throw "حجم ZIP أكبر من الحد ($MaximumPackageSizeMiB MiB)." }
        $expectedLength = $input.Length
        $output = [IO.File]::Open($Destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try {
            $input.CopyTo($output)
            $output.Flush($true)
        }
        finally { $output.Dispose() }
        if ((Get-Item -LiteralPath $Destination).Length -ne $expectedLength) { throw 'تغير طول نسخة الحزمة أثناء النسخ.' }
    }
    finally { $input.Dispose() }
}

function Test-Archive {
    param([string]$ZipPath)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        if ($archive.Entries.Count -gt $MaximumArchiveEntries) { throw "عدد عناصر ZIP أكبر من الحد المسموح ($MaximumArchiveEntries)." }
        [Int64]$totalLength = 0
        [Int64]$totalCompressedLength = 0
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        $hasExe = $false
        foreach ($entry in $archive.Entries) {
            $name = $entry.FullName.Replace('/', '\')
            if ([string]::IsNullOrWhiteSpace($name) -or $name.StartsWith('\') -or $name.StartsWith('/') -or $name -match '^[A-Za-z]:' -or $name -match ':') { throw "اسم ZIP غير آمن: $($entry.FullName)" }
            $segments = $name.Split('\', [StringSplitOptions]::RemoveEmptyEntries)
            if ($segments | Where-Object { $_ -eq '.' -or $_ -eq '..' }) { throw "ZIP يحتوي traversal: $($entry.FullName)" }
            if (-not $seen.Add($name.TrimEnd('\'))) { throw "ZIP يحتوي مساراً مكرراً: $($entry.FullName)" }
            $externalAttributes = [BitConverter]::ToUInt32([BitConverter]::GetBytes([int]$entry.ExternalAttributes), 0)
            $unixType = ($externalAttributes -shr 16) -band 0xF000
            if (($externalAttributes -band [uint32][IO.FileAttributes]::ReparsePoint) -ne 0 -or $unixType -eq 0xA000) { throw "ZIP يحتوي رابطاً أو reparse point: $($entry.FullName)" }
            if ($entry.Length -lt 0 -or $entry.Length -gt ($MaximumExtractedSizeMiB * 1MB)) { throw "حجم عنصر ZIP غير مقبول: $($entry.FullName)" }
            if ($entry.Length -gt 0 -and ($entry.Length / [Math]::Max([Int64]1, $entry.CompressedLength)) -gt $MaximumCompressionRatio) { throw "نسبة ضغط عنصر ZIP تتجاوز الحد: $($entry.FullName)" }
            $totalLength += $entry.Length
            $totalCompressedLength += $entry.CompressedLength
            if ($totalLength -gt ($MaximumExtractedSizeMiB * 1MB)) { throw "الحجم المستخرج من ZIP يتجاوز $MaximumExtractedSizeMiB MiB." }
            if ($entry.FullName -ieq 'AuditMemoSystem.App.exe' -and $entry.Length -gt 0) { $hasExe = $true }
        }
        if ($totalLength -gt 0 -and ($totalLength / [Math]::Max([Int64]1, $totalCompressedLength)) -gt $MaximumCompressionRatio) { throw 'نسبة الضغط الإجمالية لملف ZIP تتجاوز الحد.' }
        if (-not $hasExe) { throw 'الحزمة لا تحتوي AuditMemoSystem.App.exe صالحاً في جذر ZIP.' }
    }
    finally { $archive.Dispose() }
}

function Expand-SafeArchive {
    param([string]$ZipPath, [string]$Destination)
    $destinationRoot = [IO.Path]::GetFullPath($Destination).TrimEnd('\') + '\'
    $archive = [IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        foreach ($entry in $archive.Entries) {
            $relative = $entry.FullName.Replace('/', '\')
            if ($relative.EndsWith('\')) { continue }
            $target = [IO.Path]::GetFullPath((Join-Path $Destination $relative))
            if (-not $target.StartsWith($destinationRoot, [StringComparison]::OrdinalIgnoreCase)) { throw "تم رفض مسار استخراج خارج staging: $relative" }
            [IO.Directory]::CreateDirectory((Split-Path -Parent $target)) | Out-Null
            $input = $entry.Open()
            try {
                $output = [IO.File]::Open($target, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
                try { $input.CopyTo($output) } finally { $output.Dispose() }
            }
            finally { $input.Dispose() }
        }
    }
    finally { $archive.Dispose() }
}

$package = (Resolve-Path -LiteralPath $PackagePath -ErrorAction Stop).Path
$install = (Resolve-Path -LiteralPath $InstallDirectory -ErrorAction Stop).Path.TrimEnd('\')
if (-not (Test-Path -LiteralPath $package -PathType Leaf)) { throw "الحزمة غير موجودة: $PackagePath" }
if ([IO.Path]::GetExtension($package) -ine '.zip') { throw 'الحزمة يجب أن تكون ملف ZIP.' }
if (-not (Test-Path -LiteralPath $install -PathType Container)) { throw "مجلد التثبيت غير موجود: $InstallDirectory" }
$installRoot = [IO.Path]::GetPathRoot($install).TrimEnd('\')
if ([string]::IsNullOrWhiteSpace($installRoot) -or $install.Equals($installRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'لا يمكن استخدام جذر القرص كمجلد تثبيت.' }
$parent = Split-Path -Parent $install
$installName = Split-Path -Leaf $install
if ([string]::IsNullOrWhiteSpace($parent) -or [string]::IsNullOrWhiteSpace($installName) -or $installName -in @('.', '..')) { throw 'مسار التثبيت واسع أو غير صالح.' }
if (-not (Test-Path -LiteralPath $parent -PathType Container)) { throw 'المجلد الأب لمسار التثبيت غير موجود.' }
if (($installName -in @('Windows', 'Program Files', 'Program Files (x86)', 'Users', 'ProgramData', 'System32', 'System Volume Information')) -or -not (Test-Path -LiteralPath (Join-Path $install 'AuditMemoSystem.App.exe') -PathType Leaf)) { throw 'تم رفض مسار تثبيت واسع أو لا يحتوي التطبيق المتوقع في جذره.' }
$installItem = Get-Item -LiteralPath $install -Force
if (($installItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'لا يمكن ترقية مجلد تثبيت من نوع reparse point.' }

$script:LogPath = Join-Path $parent 'AuditMemoSystem-manual-update.log'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$nonce = [guid]::NewGuid().ToString('N')
$staging = Join-Path $parent "$installName.staging-$stamp-$nonce"
$backup = Join-Path $parent "$installName.backup-$stamp-$nonce"
$failed = Join-Path $parent "$installName.failed-$stamp-$nonce"
$packageSnapshot = Join-Path $parent "$installName.package-$stamp-$nonce.zip"
$snapshotGuard = $null

try {
    $expected = if ($ExpectedSha256) { $ExpectedSha256.ToUpperInvariant() } else { Get-SidecarHash $package }
    if (-not $expected) { throw 'مرر ExpectedSha256 أو ضع ملفاً جانبياً باسم <package>.zip.sha256.' }
    Copy-PackageSnapshot -Source $package -Destination $packageSnapshot -MaximumBytes ($MaximumPackageSizeMiB * 1MB)
    $snapshotGuard = [IO.File]::Open($packageSnapshot, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $actual = (Get-FileHash -LiteralPath $packageSnapshot -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($actual -ne $expected) { throw "فشل تحقق SHA-256. المتوقع $expected، الفعلي $actual" }
    Write-UpdateLog "تم تحقق SHA-256 لنسخة حزمة موثوقة من $package"
    Test-Archive $packageSnapshot
    Write-UpdateLog 'اجتازت نسخة الحزمة الموثوقة فحص ZIP الآمن.'
    $running = Get-Process -Name 'AuditMemoSystem.App' -ErrorAction SilentlyContinue
    if ($running) { throw 'أغلق AuditMemoSystem.App بالكامل قبل الترقية ثم أعد تشغيل السكربت.' }
    if ((Test-Path -LiteralPath $staging) -or (Test-Path -LiteralPath $backup) -or (Test-Path -LiteralPath $failed)) { throw 'حدث تعارض غير متوقع مع مجلد staging أو backup أو failed.' }
    if (-not $PSCmdlet.ShouldProcess($install, "ترقية يدوية من $package")) { return }
    New-Item -ItemType Directory -Path $staging -ErrorAction Stop | Out-Null
    Expand-SafeArchive -ZipPath $packageSnapshot -Destination $staging
    if (-not (Test-Path -LiteralPath (Join-Path $staging 'AuditMemoSystem.App.exe') -PathType Leaf)) { throw 'فشل التحقق بعد الاستخراج: ملف التطبيق التنفيذي غير موجود.' }
    $preserve = @('appsettings.json') + (Get-RelativeStoragePaths (Join-Path $install 'appsettings.json'))
    foreach ($relative in $preserve) {
        $source = Join-Path $install $relative
        $destination = Join-Path $staging $relative
        if (Test-Path -LiteralPath $source) {
            Copy-PreservedItemSafe -Source $source -Destination $destination
            Write-UpdateLog "حُفظ المحتوى المحلي: $relative"
        }
    }
    Move-Item -LiteralPath $install -Destination $backup -ErrorAction Stop
    try {
        Move-Item -LiteralPath $staging -Destination $install -ErrorAction Stop
        $installedExecutable = Join-Path $install 'AuditMemoSystem.App.exe'
        if (-not (Test-Path -LiteralPath $installedExecutable -PathType Leaf) -or (Get-Item -LiteralPath $installedExecutable).Length -le 0) { throw 'فشل التحقق بعد الاستبدال: ملف التطبيق التنفيذي مفقود أو فارغ.' }
        Write-UpdateLog "نجحت الترقية. النسخة السابقة محفوظة في: $backup"
        Write-Host "اكتملت الترقية. احتفظ بالنسخة الاحتياطية حتى اجتياز اختبار القبول: $backup" -ForegroundColor Green
    }
    catch {
        $swapError = $_
        Write-UpdateLog "فشل الاستبدال أو التحقق: $($swapError.Exception.Message). بدء الاسترجاع." 'ERROR'
        try {
            if (Test-Path -LiteralPath $install) {
                if (Test-Path -LiteralPath $failed) { throw "مسار failed موجود مسبقاً: $failed" }
                Move-Item -LiteralPath $install -Destination $failed -ErrorAction Stop
                Write-UpdateLog "حُفظ التثبيت الجزئي في: $failed" 'WARN'
            }
            if (-not (Test-Path -LiteralPath $backup -PathType Container)) { throw "النسخة الاحتياطية غير موجودة: $backup" }
            Move-Item -LiteralPath $backup -Destination $install -ErrorAction Stop
            if (-not (Test-Path -LiteralPath (Join-Path $install 'AuditMemoSystem.App.exe') -PathType Leaf)) { throw 'أعيد مجلد النسخة السابقة لكن ملف التطبيق التنفيذي غير موجود.' }
            Write-UpdateLog 'اكتمل الاسترجاع إلى النسخة السابقة، ولم يُحذف التثبيت الجزئي.' 'WARN'
        }
        catch {
            throw "فشل الاستبدال ($($swapError.Exception.Message)) وفشل الاسترجاع ($($_.Exception.Message)). راجع backup وfailed يدوياً."
        }
        throw $swapError
    }
}
catch {
    if ($script:LogPath) { Write-UpdateLog "فشلت الترقية: $($_.Exception.Message)" 'ERROR' }
    throw
}
finally {
    if ($null -ne $snapshotGuard) { $snapshotGuard.Dispose() }
    if ($packageSnapshot -and (Test-Path -LiteralPath $packageSnapshot -PathType Leaf)) {
        [IO.File]::Delete($packageSnapshot)
    }
}
