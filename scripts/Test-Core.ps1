[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workspaceDotnet = Join-Path $repositoryRoot '.tools\dotnet\dotnet.exe'
$dotnetCommand = if (Test-Path -LiteralPath $workspaceDotnet) {
    $workspaceDotnet
} else {
    (Get-Command dotnet -ErrorAction Stop).Source
}

$taskDotnetHome = Join-Path $repositoryRoot '.tools\dotnet-home'
$taskNugetPackages = Join-Path $repositoryRoot '.tools\nuget'
$env:DOTNET_CLI_HOME = $taskDotnetHome
$env:NUGET_PACKAGES = $taskNugetPackages
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

$project = Join-Path $repositoryRoot 'tests\Print2Md.Core.Tests\Print2Md.Core.Tests.csproj'
$nugetConfig = Join-Path $repositoryRoot 'NuGet.Config'

& $dotnetCommand restore $project --configfile $nugetConfig
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE"
}

& $dotnetCommand run --project $project --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "Core tests failed with exit code $LASTEXITCODE"
}
