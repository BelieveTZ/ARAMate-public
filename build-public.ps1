param([string]$ISCC)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build.ps1')
if (-not $ISCC) {
    $candidates = @((Join-Path $PSScriptRoot '..\..\work\inno\ISCC.exe'))
    if (${env:ProgramFiles(x86)}) { $candidates += Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe' }
    if ($env:ProgramFiles) { $candidates += Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe' }
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { $candidates += $command.Source }
    $ISCC = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}
if (-not $ISCC) { throw 'Install Inno Setup 6 or supply -ISCC <path to ISCC.exe>.' }
$binary = Join-Path $PSScriptRoot 'ARAMate.exe'
& $ISCC /DPublicRelease "/DAppBinary=$binary" (Join-Path $PSScriptRoot 'installer\setup.iss')
if ($LASTEXITCODE -ne 0) { throw 'Public installer compilation failed' }
Write-Output ('Public installer: ' + (Join-Path $PSScriptRoot 'public'))
