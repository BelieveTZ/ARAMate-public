param([string]$ISCC)
$ErrorActionPreference = 'Stop'
$dataRoot=Join-Path $PSScriptRoot '网站数据'
if (-not (Test-Path -LiteralPath (Join-Path $dataRoot 'website-state.json') -PathType Leaf)) { throw 'Website data is missing; run --sync-data before building the public installer.' }
& (Join-Path $PSScriptRoot 'build.ps1')
$gainBoard=Join-Path $PSScriptRoot 'gain-board.json'
$gainLog=Join-Path $PSScriptRoot 'build-gain-board.log'
$gainErrors=Join-Path $PSScriptRoot 'build-gain-board.error.log'
$gainProcess=Start-Process -FilePath (Join-Path $PSScriptRoot 'ARAMate.exe') -ArgumentList @('--build-gain-board',('"'+$dataRoot+'"'),('"'+$gainBoard+'"')) -Wait -PassThru -WindowStyle Hidden -RedirectStandardOutput $gainLog -RedirectStandardError $gainErrors
if ($gainProcess.ExitCode -ne 0) { throw "Gain board build failed (exit $($gainProcess.ExitCode)); see $gainLog and $gainErrors" }
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
