#Requires -Version 5.1
<#
.SYNOPSIS
  Publishes TuroClawProwl.App as a self-contained single-file exe and compiles
  the Inno Setup installer.

.EXAMPLE
  ./build.ps1
  ./build.ps1 -Version 0.2.0
#>

param(
    [string]$Configuration = "Release",
    [string]$Version
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
$repoRoot = Resolve-Path ..

if (-not $Version) {
    $csprojPath = Join-Path $repoRoot "src/TuroClawProwl.App/TuroClawProwl.App.csproj"
    $xml = [xml](Get-Content $csprojPath)
    $Version = ($xml.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
    if (-not $Version) { throw "No <Version> in $csprojPath and no -Version passed" }
}

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    throw "Inno Setup 6 not found. Install from https://jrsoftware.org/isdl.php and retry."
}

Write-Host "Version: $Version" -ForegroundColor Cyan
Write-Host "ISCC:    $iscc" -ForegroundColor Cyan

if (Test-Path stage) { Remove-Item -Recurse -Force stage }
New-Item -ItemType Directory -Path stage | Out-Null

$publishOutput = Join-Path $PSScriptRoot "stage"
dotnet publish (Join-Path $repoRoot "src/TuroClawProwl.App/TuroClawProwl.App.csproj") `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=embedded `
    -o $publishOutput `
    -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

& $iscc "/DMyAppVersion=$Version" TuroClawProwl.iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed (exit $LASTEXITCODE)" }

$output = Join-Path $PSScriptRoot "dist\TuroClawProwl-Setup-$Version-x64.exe"
Write-Host ""
Write-Host "Installer: $output" -ForegroundColor Green
