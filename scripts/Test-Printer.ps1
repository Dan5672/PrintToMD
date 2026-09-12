[CmdletBinding()]
param(
    [string] $OutputPath = (Join-Path $PSScriptRoot '..\.tools\printer-smoke.md')
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
try {
    $document.PrinterSettings.PrinterName = 'Print to Markdown'
    if (-not $document.PrinterSettings.IsValid) { throw 'Print to Markdown is not installed.' }
    $document.DocumentName = 'Print2Md synthetic smoke test'
    $document.PrinterSettings.PrintToFile = $true
    $document.PrinterSettings.PrintFileName = $destination
    $document.PrintController = New-Object System.Drawing.Printing.StandardPrintController
    $document.add_PrintPage({
        param($sender, $eventArgs)
        $eventArgs.Graphics.DrawString('Print2Md smoke test 12345', $font, [System.Drawing.Brushes]::Black, 80, 80)
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
}
