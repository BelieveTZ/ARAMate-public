param([string]$OutputFile = "ARAMate.exe", [string]$RuntimeCheckOutput = "installer\RuntimeCheck.exe")
$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
Push-Location $PSScriptRoot
try {
    New-Item -ItemType Directory -Path work -Force | Out-Null
    & $compilerPath /nologo /target:exe /out:work\PaletteExport.exe /r:System.Drawing.dll /r:System.Web.Extensions.dll ThemePalette.cs .\tools\PaletteExport.cs
    if ($LASTEXITCODE -ne 0) { throw 'Palette export compilation failed' }
    & .\work\PaletteExport.exe WebShell.html work/theme-palettes.json
    if ($LASTEXITCODE -ne 0) { throw 'Palette export failed' }
    $frameworkPath = Split-Path $compilerPath
    $metadataPath = Join-Path $env:WINDIR 'System32\WinMetadata'
    $references = @('System.Runtime.dll','System.Runtime.WindowsRuntime.dll','System.Threading.Tasks.dll') | ForEach-Object { '/r:' + (Join-Path $frameworkPath $_) }
    $references += @('Windows.Foundation','Windows.Media','Windows.Globalization','Windows.Storage','Windows.Graphics') | ForEach-Object { '/r:' + (Join-Path $metadataPath ($_+'.winmd')) }
    & $compilerPath /nologo /target:winexe /platform:x64 /resource:WebShell.html,WebShell.html /reference:lib/Microsoft.Web.WebView2.Core.dll /reference:lib/Microsoft.Web.WebView2.WinForms.dll /win32icon:AppIcon.ico /resource:EquipmentDescriptions.json,EquipmentDescriptions.json "/out:$OutputFile" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Core.dll $references AppVersion.cs ThemePalette.cs ThemeSettings.cs AutoMinimize.cs WebShell.cs TablePresentation.cs DesktopTheme.cs BadgePaint.cs ConfidenceCore.cs GainBoardCache.cs GameLocation.cs EffectText.cs InferredRoutes.cs SpecialRoutes.cs RouteRecommendations.cs EquipmentRecommendations.cs LaterScoring.cs CombinationShopBuilds.cs ShopBuilds.cs WebsiteData.cs LocalClient.cs HudSkillOverlay.cs OcrOverlay.cs SelectionOverlay.cs OverlayTests.cs ReplayForm.cs Program.cs
    if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
    & $compilerPath /nologo /target:exe /platform:x64 "/out:$RuntimeCheckOutput" $references .\installer\RuntimeCheck.cs
    if ($LASTEXITCODE -ne 0) { throw 'Runtime check compilation failed' }
    $destination=Split-Path ([IO.Path]::GetFullPath($OutputFile))
    Get-ChildItem (Join-Path $PSScriptRoot 'lib') -File | Copy-Item -Destination $destination -Force
} finally { Pop-Location }






