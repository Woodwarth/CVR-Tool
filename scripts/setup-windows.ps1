param([switch]$InstallMissing, [switch]$InstallExtensions, [switch]$OpenProject)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $root
function Update-ProcessPath {
    $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' +
        [Environment]::GetEnvironmentVariable('Path', 'User')
}
function Install-Package([string]$id, [string]$scope) {
    if (-not $InstallMissing) { throw "Fehlt: $id. Erneut mit -InstallMissing starten." }
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        throw "winget fehlt. $id bitte manuell installieren und erneut starten."
    }
    $wingetArgs = @('install', '--exact', '--id', $id, '--source', 'winget',
        '--accept-source-agreements', '--accept-package-agreements')
    if ($scope) { $wingetArgs += @('--scope', $scope) }
    & winget @wingetArgs
    if ($LASTEXITCODE -ne 0) { throw "Installation von $id fehlgeschlagen." }
    Update-ProcessPath
}
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
$hasSdk = $false
if ($dotnet) { $hasSdk = @(& dotnet --list-sdks | Where-Object { $_ -match '^10\.' }).Count -gt 0 }
if (-not $hasSdk) { Install-Package 'Microsoft.DotNet.SDK.10' '' }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET nicht gefunden. Terminal neu starten.' }
if (-not (@(& dotnet --list-sdks | Where-Object { $_ -match '^10\.' }).Count -gt 0)) {
    throw '.NET 10 SDK nicht gefunden.'
}
$code = Get-Command code -ErrorAction SilentlyContinue
if (-not $code) {
    $candidate = Join-Path $env:LOCALAPPDATA 'Programs\Microsoft VS Code\bin\code.cmd'
    if (Test-Path -LiteralPath $candidate) { $code = Get-Item -LiteralPath $candidate }
}
if (-not $code) {
    Install-Package 'Microsoft.VisualStudioCode' 'user'
    $code = Get-Command code -ErrorAction SilentlyContinue
    if (-not $code) {
        $candidate = Join-Path $env:LOCALAPPDATA 'Programs\Microsoft VS Code\bin\code.cmd'
        if (Test-Path -LiteralPath $candidate) { $code = Get-Item -LiteralPath $candidate }
    }
}
if (-not $code) { throw 'VS Code nicht gefunden. Terminal neu starten.' }
$codePath = if ($code.Source) { $code.Source } else { $code.FullName }
if ($InstallExtensions) {
    foreach ($extension in @('ms-dotnettools.csharp','AvaloniaTeam.vscode-avalonia','openai.chatgpt')) {
        & $codePath --install-extension $extension
        if ($LASTEXITCODE -ne 0) { throw "VS-Code-Erweiterung konnte nicht installiert werden: $extension" }
    }
}
& dotnet restore CvrTool.slnx --configfile NuGet.Config
if ($LASTEXITCODE -ne 0) { throw 'NuGet-Restore fehlgeschlagen.' }
& dotnet build CvrTool.slnx --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Build fehlgeschlagen.' }
& dotnet run --project tests/CvrTool.Checks --no-build
if ($LASTEXITCODE -ne 0) { throw 'Verhaltenspruefungen fehlgeschlagen.' }

# Only project-local configuration; existing unrelated settings are preserved.
$assembly = Join-Path $root 'src\CvrTool.Mcp\bin\Debug\net10.0\CvrTool.Mcp.dll'
$dotnetPath = (Get-Command dotnet).Source
$codexDirectory = Join-Path $root '.codex'
New-Item -ItemType Directory -Path $codexDirectory -Force | Out-Null
$codexConfig = Join-Path $codexDirectory 'config.toml'
$section = '[mcp_servers.cvr_tool_demo]'
$existingConfig = if (Test-Path -LiteralPath $codexConfig) { [string](Get-Content -LiteralPath $codexConfig -Raw) } else { '' }
if ($existingConfig.Contains($section)) {
    Write-Host 'Vorhandene CVR-MCP-Konfiguration bleibt erhalten. Bei Ordnerwechsel Pfade pruefen.'
} else {
    $toml = "`n$section`ncommand = $($dotnetPath | ConvertTo-Json -Compress)`nargs = [$($assembly | ConvertTo-Json -Compress)]`nstartup_timeout_sec = 30`n"
    [System.IO.File]::WriteAllText($codexConfig, $existingConfig + $toml, (New-Object System.Text.UTF8Encoding($false)))
}
$claudeConfig = Join-Path $root '.mcp.json'
$claude = if (Test-Path -LiteralPath $claudeConfig) { Get-Content -LiteralPath $claudeConfig -Raw | ConvertFrom-Json } else { [pscustomobject]@{} }
if (-not $claude.PSObject.Properties['mcpServers']) {
    $claude | Add-Member -NotePropertyName mcpServers -NotePropertyValue ([pscustomobject]@{})
}
if (-not $claude.mcpServers.PSObject.Properties['cvr-tool-demo']) {
    $claude.mcpServers | Add-Member -NotePropertyName 'cvr-tool-demo' -NotePropertyValue ([pscustomobject]@{
        command = $dotnetPath; args = @($assembly)
    })
    [System.IO.File]::WriteAllText($claudeConfig, ($claude | ConvertTo-Json -Depth 20), (New-Object System.Text.UTF8Encoding($false)))
}
Write-Host 'Build und Pruefungen erfolgreich. MCP lokal fuer dieses Projekt konfiguriert.'
Write-Host 'Codex: Projekt vertrauen, Codex-Seitenleiste oeffnen und anmelden.'
Write-Host 'Claude Code: Projekt-MCP-Konfiguration beim naechsten Start bestaetigen.'
if ($OpenProject) { & $codePath (Join-Path $root 'CVR-Tool.code-workspace') }
