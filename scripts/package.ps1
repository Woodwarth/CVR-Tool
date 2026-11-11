param([Parameter(Mandatory)][ValidateSet('win-x64','win-arm64','osx-arm64')][string]$Runtime)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
[xml]$props = Get-Content Directory.Build.props
$version = [string]$props.Project.PropertyGroup.Version
$stage = Join-Path $PWD "artifacts/stage-$version-$Runtime"
if (Test-Path -LiteralPath $stage) { throw "Stage already exists: $stage. Use a fresh build directory." }
New-Item -ItemType Directory -Path $stage -Force | Out-Null
$desktop = Join-Path $stage 'desktop'
dotnet publish src/CvrTool.Desktop -c Release -r $Runtime --self-contained true -p:PublishTrimmed=false -o $desktop --configfile NuGet.Config
if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed' }
# Desktop already contains the integrated MCP host and optional MCP apphost.
# Ship one self-contained runtime; native NuGet debug symbols are not runtime assets.
$desktopRoot = (Resolve-Path -LiteralPath $desktop).Path
Get-ChildItem -LiteralPath $desktopRoot -Recurse -File -Filter '*.pdb' | ForEach-Object {
    if (-not $_.FullName.StartsWith($desktopRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Debug symbol path escaped the package directory'
    }
    Remove-Item -LiteralPath $_.FullName
}
Copy-Item LICENSE,README.md,CHANGELOG.md -Destination $stage
$previewDirectory = Join-Path $stage 'docs/images'
New-Item -ItemType Directory -Path $previewDirectory -Force | Out-Null
Copy-Item -LiteralPath 'docs/images/outputs.png' -Destination $previewDirectory
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
The optional CvrTool.Mcp executable beside the desktop executable shares the
same included runtime. There is no second mcp/ runtime directory.
For headless ownership use --host-session. Manual CVR targets are configured
with the integrated MCP tool connect_amplifier. No network discovery or firmware updates.
Updates: choose Updates > Check for updates > Download and restart in the app.
Packages are verified and installed in your user profile; previous versions stay.
Export local session settings before restarting. Version 0.3.0 requires one manual
upgrade to 0.4.0 before this feature is available.
Local demo devices remain available; physical-amplifier validation is pending.
'@
[System.IO.File]::WriteAllText((Join-Path $stage 'START-HERE.md'), $startReadme, (New-Object System.Text.UTF8Encoding($false)))
function Assert-CompactPackage {
    $files = @(Get-ChildItem -LiteralPath $stage -Recurse -File)
    if (@($files | Where-Object Extension -eq '.pdb').Count -ne 0) { throw 'Debug symbols must not ship in release packages' }
    if (@($files | Where-Object Name -in @('coreclr.dll','libcoreclr.dylib')).Count -ne 1) { throw 'Package must contain exactly one shared runtime' }
}
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
    chmod +x (Join-Path $macos 'CvrTool.Desktop') (Join-Path $macos 'CvrTool.Mcp')
    if ($LASTEXITCODE -ne 0) { throw 'chmod failed' }
    codesign --force --deep --sign - $app
    if ($LASTEXITCODE -ne 0) { throw 'Ad-hoc codesign failed' }
    Assert-CompactPackage
    $archive = Join-Path $PWD "artifacts/CVR-Tool-$version-$Runtime.tar.gz"
    # Use standard .NET tar records instead of macOS-specific extended headers.
    Add-Type -AssemblyName System.Formats.Tar
    $archiveStream = [System.IO.File]::Create($archive)
    try {
        $gzip = [System.IO.Compression.GZipStream]::new($archiveStream, [System.IO.Compression.CompressionLevel]::Optimal, $true)
        try { [System.Formats.Tar.TarFile]::CreateFromDirectory($stage, $gzip, $false) }
        finally { $gzip.Dispose() }
    } finally { $archiveStream.Dispose() }
} else {
    $launcher = "@echo off`r`nstart `"`" `"%~dp0desktop\CvrTool.Desktop.exe`"`r`n"
    [System.IO.File]::WriteAllText((Join-Path $stage 'START-CVR-TOOL.cmd'), $launcher, (New-Object System.Text.UTF8Encoding($false)))
    Assert-CompactPackage
    Compress-Archive -Path "$stage/*" -DestinationPath "artifacts/CVR-Tool-$version-$Runtime.zip"
}
