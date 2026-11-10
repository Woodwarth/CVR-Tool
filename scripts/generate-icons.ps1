# Development helper: render the fader mark as Windows and macOS icons. No runtime dependency.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$assets = Join-Path $root 'src/CvrTool.Desktop/Assets'
$cache = Join-Path $root 'artifacts/icon-build'
New-Item -ItemType Directory -Path $assets,$cache -Force | Out-Null
$svg = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1024 1024"><rect x="64" y="64" width="896" height="896" rx="192" fill="#008e87"/><path d="M320 256v512 M512 256v512 M704 256v512" fill="none" stroke="#edf2f5" stroke-width="44" stroke-linecap="round"/><g fill="#edf2f5" stroke="#008e87" stroke-width="24"><rect x="244" y="340" width="152" height="116"/><rect x="436" y="568" width="152" height="116"/><rect x="628" y="404" width="152" height="116"/></g></svg>'
[System.IO.File]::WriteAllText((Join-Path $assets 'AppIcon.svg'), $svg)
foreach ($size in @(16,24,32,48,64,128,256,512,1024)) {
    $bitmap = [System.Drawing.Bitmap]::new($size,$size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform($size / 1024.0, $size / 1024.0)
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc(64,64,384,384,180,90); $path.AddArc(576,64,384,384,270,90)
    $path.AddArc(576,576,384,384,0,90); $path.AddArc(64,576,384,384,90,90); $path.CloseFigure()
    $teal = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#008E87'))
    $white = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#EDF2F5'))
    $pen = [System.Drawing.Pen]::new($white,44)
    $pen.StartCap = $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $graphics.FillPath($teal,$path)
    foreach ($x in @(320,512,704)) { $graphics.DrawLine($pen,$x,256,$x,768) }
    foreach ($knob in @(@(244,340),@(436,568),@(628,404))) {
        $graphics.FillRectangle($teal,$knob[0]-10,$knob[1]-10,172,136)
        $graphics.FillRectangle($white,$knob[0],$knob[1],152,116)
    }
    $bitmap.Save((Join-Path $cache "$size.png"),[System.Drawing.Imaging.ImageFormat]::Png)
    $pen.Dispose(); $teal.Dispose(); $white.Dispose(); $path.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
Copy-Item -LiteralPath (Join-Path $cache '1024.png') -Destination (Join-Path $assets 'AppIcon.png')
$ico = [System.IO.MemoryStream]::new(); $writer = [System.IO.BinaryWriter]::new($ico)
$sizes = @(16,24,32,48,64,128,256)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($size in $sizes) {
    $bytes = [System.IO.File]::ReadAllBytes((Join-Path $cache "$size.png"))
    $dimension = if ($size -eq 256) { 0 } else { $size }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$bytes.Length); $writer.Write([uint32]$offset)
    $offset += $bytes.Length
}
foreach ($size in $sizes) { $writer.Write([System.IO.File]::ReadAllBytes((Join-Path $cache "$size.png"))) }
[System.IO.File]::WriteAllBytes((Join-Path $assets 'AppIcon.ico'), $ico.ToArray())
$writer.Dispose(); $ico.Dispose()
$icns = [System.IO.MemoryStream]::new()
foreach ($entry in @(@('icp4',16),@('icp5',32),@('icp6',64),@('ic07',128),@('ic08',256),@('ic09',512),@('ic10',1024))) {
    $bytes = [System.IO.File]::ReadAllBytes((Join-Path $cache "$($entry[1]).png"))
    $icns.Write([System.Text.Encoding]::ASCII.GetBytes($entry[0]))
    $length = [System.BitConverter]::GetBytes([uint32]($bytes.Length+8)); [Array]::Reverse($length); $icns.Write($length); $icns.Write($bytes)
}
$result = [System.IO.MemoryStream]::new(); $result.Write([System.Text.Encoding]::ASCII.GetBytes('icns'))
$length = [System.BitConverter]::GetBytes([uint32]($icns.Length+8)); [Array]::Reverse($length); $result.Write($length); $result.Write($icns.ToArray())
[System.IO.File]::WriteAllBytes((Join-Path $assets 'AppIcon.icns'), $result.ToArray())
$result.Dispose(); $icns.Dispose()
