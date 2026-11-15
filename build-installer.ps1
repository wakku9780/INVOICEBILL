<#
.SYNOPSIS
    Builds the Saree Billing installer: runs the tests, publishes a self-contained
    64-bit release, and compiles installer\SareeBilling.iss with Inno Setup 6.

.EXAMPLE
    .\build-installer.ps1
    Output: artifacts\installer\SareeBilling-Setup-<version>.exe

.NOTES
    The version comes from Directory.Build.props (<Version>). Increase it before each
    release so Windows shows the update correctly.
#>
[CmdletBinding()]
param(
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$version = ([xml](Get-Content 'Directory.Build.props')).Project.PropertyGroup.Version
Write-Host "Building Saree Billing $version" -ForegroundColor Cyan

if (-not $SkipTests) {
    Write-Host "`n[1/3] Running tests..." -ForegroundColor Cyan
    dotnet test --configuration Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed. The installer was not built.' }
}

Write-Host "`n[2/3] Publishing self-contained release..." -ForegroundColor Cyan
if (Test-Path 'artifacts\publish') { Remove-Item 'artifacts\publish' -Recurse -Force }
dotnet publish 'src\SareeBilling.Desktop' -p:PublishProfile=win-x64 --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

Write-Host "`n[3/3] Building installer..." -ForegroundColor Cyan
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    throw 'Inno Setup 6 was not found. Install it from https://jrsoftware.org/isdl.php (or: winget install JRSoftware.InnoSetup), then run this script again.'
}

& $iscc "/DMyAppVersion=$version" 'installer\SareeBilling.iss'
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed.' }

$setup = Get-Item "artifacts\installer\SareeBilling-Setup-$version.exe"
Write-Host "`nDone: $($setup.FullName)  ($([math]::Round($setup.Length / 1MB, 1)) MB)" -ForegroundColor Green
