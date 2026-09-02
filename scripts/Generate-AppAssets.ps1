[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$assetDirectory = Join-Path $repositoryRoot 'src\Print2Md.App\Assets'
$sourceDirectory = Join-Path $assetDirectory 'Source'
$logoSource = Join-Path $sourceDirectory 'Logo.png'
$splashSource = Join-Path $sourceDirectory 'SplashScreen.png'

if (-not (Test-Path -LiteralPath $logoSource) -or -not (Test-Path -LiteralPath $splashSource)) {
    throw 'The generated source logo and splash images are missing.'
}

function Write-ScaledImage {
    param(
        [Parameter(Mandatory)] [string] $Source,
        [Parameter(Mandatory)] [string] $Destination,
        [Parameter(Mandatory)] [int] $Width,
        [Parameter(Mandatory)] [int] $Height,
        [switch] $CenterCrop
    )

    $sourceImage = [System.Drawing.Image]::FromFile($Source)
    try {
        $destinationImage = New-Object System.Drawing.Bitmap($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $destinationImage.SetResolution(96, 96)
            $graphics = [System.Drawing.Graphics]::FromImage($destinationImage)
            try {
                $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
                $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $graphics.Clear([System.Drawing.Color]::Transparent)

                $sourceWidth = [double]$sourceImage.Width
                $sourceHeight = [double]$sourceImage.Height
                $targetWidth = [double]$Width
                $targetHeight = [double]$Height
                $sourceRectangle = [System.Drawing.RectangleF]::new(0, 0, [single]$sourceWidth, [single]$sourceHeight)
                if ($CenterCrop) {
                    $sourceRatio = $sourceWidth / $sourceHeight
                    $targetRatio = $targetWidth / $targetHeight
                    if ($sourceRatio -gt $targetRatio) {
                        $cropWidth = $sourceHeight * $targetRatio
                        $sourceRectangle = [System.Drawing.RectangleF]::new([single](($sourceWidth - $cropWidth) / 2), 0, [single]$cropWidth, [single]$sourceHeight)
                    } else {
                        $cropHeight = $sourceWidth / $targetRatio
                        $sourceRectangle = [System.Drawing.RectangleF]::new(0, [single](($sourceHeight - $cropHeight) / 2), [single]$sourceWidth, [single]$cropHeight)
                    }
                }

                $destinationRectangle = [System.Drawing.RectangleF]::new(0, 0, [single]$Width, [single]$Height)
                $graphics.DrawImage($sourceImage, $destinationRectangle, $sourceRectangle, [System.Drawing.GraphicsUnit]::Pixel)
            } finally {
                $graphics.Dispose()
            }

            $destinationImage.Save($Destination, [System.Drawing.Imaging.ImageFormat]::Png)
        } finally {
            $destinationImage.Dispose()
        }
    } finally {
        $sourceImage.Dispose()
    }
}

Write-ScaledImage -Source $logoSource -Destination (Join-Path $assetDirectory 'Square44x44Logo.png') -Width 44 -Height 44
Write-ScaledImage -Source $logoSource -Destination (Join-Path $assetDirectory 'Square150x150Logo.png') -Width 150 -Height 150
Write-ScaledImage -Source $logoSource -Destination (Join-Path $assetDirectory 'StoreLogo.png') -Width 50 -Height 50
Write-ScaledImage -Source $splashSource -Destination (Join-Path $assetDirectory 'Wide310x150Logo.png') -Width 310 -Height 150 -CenterCrop
Write-ScaledImage -Source $splashSource -Destination (Join-Path $assetDirectory 'SplashScreen.png') -Width 620 -Height 300 -CenterCrop

Write-Output 'Generated exact-size MSIX visual assets.'
