$ErrorActionPreference = 'Stop'
$forbidden = @('bin', 'obj', '.codex-temp', 'Data', 'Backups')
$tracked = git ls-files
foreach ($path in $tracked) {
    $parts = $path -split '[\\/]'
    if ($parts | Where-Object { $forbidden -contains $_ }) { throw "Tracked generated or runtime path: $path" }
    if ($path -match '\.(backup|pfx|key)$|(^|/)(\.env)$') { throw "Tracked secret or backup path: $path" }
}
Write-Host 'Source hygiene check passed.'
