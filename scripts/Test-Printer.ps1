[CmdletBinding()]
param(
    [string] $OutputPath = (Join-Path $PSScriptRoot '..\.tools\printer-smoke.md'),
    [ValidateSet('Text', 'Raster', 'Outline')]
    [string] $Mode = 'Text'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$destination = [System.IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $destination) {
    throw "Choose a fresh output path; this test will not overwrite an existing file: $destination"
}
New-Item -ItemType Directory -Path ([System.IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
$document = New-Object System.Drawing.Printing.PrintDocument
$font = New-Object System.Drawing.Font('Arial', 12)
$raster = $null
$outline = $null
try {
    if ($Mode -eq 'Raster') {
        $raster = New-Object System.Drawing.Bitmap(1200, 180)
        $graphics = [System.Drawing.Graphics]::FromImage($raster)
        $rasterFont = New-Object System.Drawing.Font('Arial', 36)
        try {
            $graphics.Clear([System.Drawing.Color]::White)
            $graphics.DrawString('Print2Md smoke test 12345', $rasterFont, [System.Drawing.Brushes]::Black, 20, 45)
        } finally {
            $graphics.Dispose()
            $rasterFont.Dispose()
        }
    }
    if ($Mode -eq 'Outline') {
        $outline = New-Object System.Drawing.Drawing2D.GraphicsPath
        $outline.AddString('Print2Md smoke test 12345', $font.FontFamily, 0, 24,
            [System.Drawing.PointF]::new(80, 80), [System.Drawing.StringFormat]::GenericDefault)
    }
    $document.PrinterSettings.PrinterName = 'Print to Markdown'
    if (-not $document.PrinterSettings.IsValid) { throw 'Print to Markdown is not installed.' }
    $document.DocumentName = 'Print2Md synthetic smoke test'
    $document.PrinterSettings.PrintToFile = $true
    $document.PrinterSettings.PrintFileName = $destination
    $document.PrintController = New-Object System.Drawing.Printing.StandardPrintController
    $document.add_PrintPage({
        param($sender, $eventArgs)
        if ($Mode -eq 'Raster') {
            $eventArgs.Graphics.DrawImage($raster, 80, 80, 600, 90)
        } elseif ($Mode -eq 'Outline') {
            $eventArgs.Graphics.FillPath([System.Drawing.Brushes]::Black, $outline)
        } else {
            $eventArgs.Graphics.DrawString('Print2Md smoke test 12345', $font, [System.Drawing.Brushes]::Black, 80, 80)
        }
        $eventArgs.HasMorePages = $false
    })
    $document.Print()
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        try {
            if ((Test-Path -LiteralPath $destination) -and
                ([System.IO.File]::ReadAllText($destination).Contains('Print2Md smoke test 12345'))) {
                Write-Output "PASS: printed text recovered in $destination"
                return
            }
        } catch [System.IO.IOException] {
            # The background task may still be writing the target.
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "The print job did not produce the expected text within 30 seconds. Check print2md.log. Expected output: $destination"
} finally {
    $document.Dispose()
    $font.Dispose()
    if ($null -ne $raster) { $raster.Dispose() }
    if ($null -ne $outline) { $outline.Dispose() }
}
