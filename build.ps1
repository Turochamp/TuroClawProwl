#Requires -Version 5
param(
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location $root

Write-Host "[1/3] dotnet test" -ForegroundColor Cyan
& dotnet test --nologo --logger "console;verbosity=minimal"
if ($LASTEXITCODE -ne 0) { throw "Tests failed." }

Write-Host "[2/3] dotnet publish" -ForegroundColor Cyan
$publishDir = Join-Path $root 'publish'
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
& dotnet publish "$root\src\TuroClawProwl.App\TuroClawProwl.App.csproj" `
    -c Release -r win-x64 --self-contained true `
    -o $publishDir --nologo
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

if ($SkipInstaller) {
    Write-Host "Installer build skipped (-SkipInstaller)." -ForegroundColor Yellow
    return
}

Write-Host "[3/3] Inno Setup" -ForegroundColor Cyan
$iscc = (Get-Command 'iscc.exe' -ErrorAction SilentlyContinue)?.Source
if (-not $iscc) {
    $candidate = 'C:\Program Files (x86)\Inno Setup 6\iscc.exe'
    if (Test-Path $candidate) { $iscc = $candidate }
}
if (-not $iscc) {
    Write-Warning "iscc.exe not found. Install Inno Setup 6 or skip with -SkipInstaller."
    return
}
& $iscc "$root\installer\installer.iss"
if ($LASTEXITCODE -ne 0) { throw "Installer build failed." }

Write-Host "Done. Installer output in $root\dist" -ForegroundColor Green
