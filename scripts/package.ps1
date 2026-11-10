param([Parameter(Mandatory)][ValidateSet('win-x64','win-arm64','osx-arm64')][string]$Runtime)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
[xml]$props = Get-Content Directory.Build.props
$version = [string]$props.Project.PropertyGroup.Version
$stage = Join-Path $PWD "artifacts/stage-$Runtime"
if (Test-Path -LiteralPath $stage) { throw "Stage already exists: $stage. Use a fresh build directory." }
New-Item -ItemType Directory -Path $stage -Force | Out-Null
$desktop = Join-Path $stage 'desktop'
$mcp = Join-Path $stage 'mcp'
dotnet publish src/CvrTool.Desktop -c Release -r $Runtime --self-contained true -p:PublishTrimmed=false -o $desktop --configfile NuGet.Config
if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed' }
dotnet publish src/CvrTool.Mcp -c Release -r $Runtime --self-contained true -p:PublishTrimmed=false -o $mcp --configfile NuGet.Config
if ($LASTEXITCODE -ne 0) { throw 'MCP publish failed' }
Copy-Item LICENSE,README.md,CHANGELOG.md -Destination $stage
$startReadme = @'
# CVR Tool

This is a self-contained desktop application. No .NET installation is required.

Windows: extract the complete ZIP and run START-CVR-TOOL.cmd, or launch
desktop/CvrTool.Desktop.exe. Keep all files together.

macOS (Apple Silicon): extract the archive and open CVR Tool.app.
The app is ad-hoc signed, not notarized
or signed with an Apple Developer ID. macOS may prevent it from opening.

The desktop executable supports --mcp for integrated stdio MCP. Start the GUI
first, then connect your MCP client with that argument. Both use one session.
The separate executable in mcp/ is an alternative client of the same session.
For headless ownership use --host-session. Manual CVR targets are configured
in Network or with connect_amplifier. No network discovery or firmware updates.
Local demo devices remain available; physical-amplifier validation is pending.
'@
[System.IO.File]::WriteAllText((Join-Path $stage 'START-HERE.md'), $startReadme, (New-Object System.Text.UTF8Encoding($false)))
if ($Runtime.StartsWith('osx-')) {
    if (-not $IsMacOS) { throw 'Mac bundles must be packaged and checked on macOS.' }
    $app = Join-Path $stage 'CVR Tool.app'
    $macos = Join-Path $app 'Contents/MacOS'
    $resources = Join-Path $app 'Contents/Resources'
    New-Item -ItemType Directory -Path $resources -Force | Out-Null
    Copy-Item -LiteralPath 'src/CvrTool.Desktop/Assets/AppIcon.icns' -Destination $resources
    New-Item -ItemType Directory -Path $macos -Force | Out-Null
    Get-ChildItem -LiteralPath $desktop | Move-Item -Destination $macos
    $plist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleIdentifier</key><string>org.cvrtool.desktop</string>
<key>CFBundleName</key><string>CVR Tool</string>
<key>CFBundleExecutable</key><string>CvrTool.Desktop</string>
<key>CFBundleIconFile</key><string>AppIcon.icns</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleShortVersionString</key><string>$version</string>
<key>CFBundleVersion</key><string>$version</string>
<key>NSHighResolutionCapable</key><true/>
</dict></plist>
"@
    Set-Content -LiteralPath (Join-Path $app 'Contents/Info.plist') -Value $plist -Encoding utf8NoBOM
    chmod +x (Join-Path $macos 'CvrTool.Desktop') (Join-Path $mcp 'CvrTool.Mcp')
    if ($LASTEXITCODE -ne 0) { throw 'chmod failed' }
    codesign --force --deep --sign - $app
    if ($LASTEXITCODE -ne 0) { throw 'Ad-hoc codesign failed' }
    $archive = Join-Path $PWD "artifacts/CVR-Tool-$version-$Runtime.tar.gz"
    tar -czf $archive -C $stage .
    if ($LASTEXITCODE -ne 0) { throw 'Archive failed' }
} else {
    $launcher = "@echo off`r`nstart `"`" `"%~dp0desktop\CvrTool.Desktop.exe`"`r`n"
    [System.IO.File]::WriteAllText((Join-Path $stage 'START-CVR-TOOL.cmd'), $launcher, (New-Object System.Text.UTF8Encoding($false)))
    Compress-Archive -Path "$stage/*" -DestinationPath "artifacts/CVR-Tool-$version-$Runtime.zip"
}
