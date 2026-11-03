# Builds dist\Aurora.exe: one file, runs on a clean Windows 10/11 x64, nothing to install.
# Telegram API keys come from src\Aurora\aurora.keys.props or AURORA_API_ID / AURORA_API_HASH.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$keys = Join-Path $root 'src\Aurora\aurora.keys.props'
if (-not (Test-Path $keys) -and -not $env:AURORA_API_ID) {
    Write-Warning 'No API keys found: this build will ask users for api_id/api_hash on first start.'
}
dotnet publish (Join-Path $root 'src\Aurora') -c Release -r win-x64 -o (Join-Path $root 'dist')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Get-Item (Join-Path $root 'dist\Aurora.exe') | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
