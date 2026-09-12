[CmdletBinding()]
param(
    [string] $PackagePath,
    [switch] $Install
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$manifestPath = Join-Path $repositoryRoot 'src\Print2Md.App\Package.appxmanifest'

if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "The application manifest was not found at $manifestPath."
}

[xml] $manifest = Get-Content -LiteralPath $manifestPath
$packageName = $manifest.Package.Identity.Name
$certificateSubject = $manifest.Package.Identity.Publisher

function Resolve-PackagePath {
    param(
        [string] $Requested
    )

    if ($Requested) {
        if (-not (Test-Path -LiteralPath $Requested)) {
            throw "The package $Requested was not found."
        }

        return [System.IO.Path]::GetFullPath($Requested)
    }

    $searchRoots = @(
        (Join-Path $repositoryRoot 'artifacts'),
        (Join-Path $env:USERPROFILE 'Downloads')
    )

    $candidate = $searchRoots |
        Where-Object { Test-Path -LiteralPath $_ } |
        ForEach-Object { Get-ChildItem -LiteralPath $_ -Recurse -Filter '*.msix' -ErrorAction SilentlyContinue } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if (-not $candidate) {
        throw 'No .msix was found under artifacts or Downloads. Pass -PackagePath explicitly.'
    }

    return $candidate.FullName
}

function Resolve-SignTool {
    $installed = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Recurse -Filter 'signtool.exe' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -like '*\x64\*' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1

    if ($installed) {
        return $installed.FullName
    }

    $toolDirectory = Join-Path $repositoryRoot '.tools\sdk'
    $cached = Get-ChildItem $toolDirectory -Recurse -Filter 'signtool.exe' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -like '*\x64\*' } |
        Select-Object -First 1

    if ($cached) {
        return $cached.FullName
    }

    Write-Verbose 'Downloading the Windows SDK build tools for signtool.exe.'
    New-Item -ItemType Directory -Path $toolDirectory -Force | Out-Null
    $archive = Join-Path $toolDirectory 'sdk-build-tools.zip'
    Invoke-WebRequest -Uri 'https://www.nuget.org/api/v2/package/Microsoft.Windows.SDK.BuildTools' -OutFile $archive -UseBasicParsing
    Expand-Archive -LiteralPath $archive -DestinationPath $toolDirectory -Force

    $downloaded = Get-ChildItem $toolDirectory -Recurse -Filter 'signtool.exe' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -like '*\x64\*' } |
        Select-Object -First 1

    if (-not $downloaded) {
        throw 'signtool.exe was not found in the downloaded Windows SDK build tools.'
    }

    return $downloaded.FullName
}

function Resolve-SigningCertificate {
    $existing = Get-ChildItem Cert:\CurrentUser\My |
        Where-Object { $_.Subject -eq $certificateSubject -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1

    if ($existing) {
        return $existing
    }

    Write-Verbose "Creating a development signing certificate for $certificateSubject."
    return New-SelfSignedCertificate `
        -Type Custom `
        -Subject $certificateSubject `
        -KeyUsage DigitalSignature `
        -FriendlyName 'Print to Markdown development signing' `
        -CertStoreLocation Cert:\CurrentUser\My `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}Subject Type:End Entity') `
        -NotAfter (Get-Date).AddYears(3)
}

function Assert-MachineTrust {
    param(
        [Parameter(Mandatory)] [System.Security.Cryptography.X509Certificates.X509Certificate2] $Certificate
    )

    $trusted = Get-ChildItem Cert:\LocalMachine\TrustedPeople -ErrorAction SilentlyContinue |
        Where-Object { $_.Thumbprint -eq $Certificate.Thumbprint }

    if ($trusted) {
        return
    }

    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    $exportPath = Join-Path $repositoryRoot '.tools\Print2Md-development.cer'
    New-Item -ItemType Directory -Path (Split-Path -Parent $exportPath) -Force | Out-Null
    Export-Certificate -Cert $Certificate -FilePath $exportPath -Force | Out-Null

    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw @"
The signing certificate is not trusted by this machine and trusting it requires elevation.
Run the following from an elevated PowerShell prompt, then run this script again:

    Import-Certificate -FilePath '$exportPath' -CertStoreLocation Cert:\LocalMachine\TrustedPeople
"@
    }

    Import-Certificate -FilePath $exportPath -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
}

$sourcePackage = Resolve-PackagePath -Requested $PackagePath
$signTool = Resolve-SignTool
$certificate = Resolve-SigningCertificate
Assert-MachineTrust -Certificate $certificate

$signedPackage = Join-Path ([System.IO.Path]::GetDirectoryName($sourcePackage)) `
    ([System.IO.Path]::GetFileNameWithoutExtension($sourcePackage) + '-signed.msix')
Copy-Item -LiteralPath $sourcePackage -Destination $signedPackage -Force

& $signTool sign /fd SHA256 /sha1 $certificate.Thumbprint /tr http://timestamp.digicert.com /td SHA256 $signedPackage
if ($LASTEXITCODE -ne 0) {
    throw "Signing failed with exit code $LASTEXITCODE."
}

& $signTool verify /pa $signedPackage
if ($LASTEXITCODE -ne 0) {
    throw "Signature verification failed with exit code $LASTEXITCODE."
}

if ($Install) {
    # Prefer an in-place upgrade. Uninstalling moves the virtual printer queue into
    # PendingDeletion, and a queue still holding a job cannot finish deleting, which
    # leaves the printer unusable until the spooler is restarted. Removing first is
    # only needed when the package version has not changed.
    try {
        Add-AppxPackage -Path $signedPackage -ErrorAction Stop
    }
    catch {
        Write-Verbose 'In-place install failed; removing the installed package first.'
        $installed = Get-AppxPackage -Name $packageName
        if ($installed) {
            Remove-AppxPackage -Package $installed.PackageFullName
        }

        Add-AppxPackage -Path $signedPackage
    }

    $queue = Get-Printer -ErrorAction SilentlyContinue | Where-Object { $_.PortName -like "$packageName`_*" }
    if ($queue) {
        Write-Output "Installed virtual printer: $($queue.Name)"
    } else {
        Write-Warning 'The package installed but no virtual printer queue was created. Check Get-Printer.'
    }
}

Write-Output $signedPackage
