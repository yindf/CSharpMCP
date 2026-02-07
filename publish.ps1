#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Publish CSharpMcp.Server as a self-contained executable

.DESCRIPTION
    Creates a standalone executable that doesn't require .NET runtime to be installed.
    Supports multiple platforms and configurations.

.EXAMPLE
    .\publish.ps1
    Publish for current platform (default)

.EXAMPLE
    .\publish.ps1 -Runtime win-x64
    Publish for Windows x64

.EXAMPLE
    .\publish.ps1 -Runtime linux-x64 -OutputDir publish/linux
    Publish for Linux x64 to custom output directory
#>

param(
    [string]$Runtime = $null,
    [string]$OutputDir = "publish",
    [switch]$NoTrim = $false,
    [switch]$NoReadyToRun = $false,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

# Detect runtime if not specified
if ([string]::IsNullOrEmpty($Runtime)) {
    if ($IsWindows -or $env:OS -eq "Windows_NT") {
        $Runtime = "win-x64"
    } elseif ($IsLinux) {
        $Runtime = "linux-x64"
    } elseif ($IsMacOS) {
        $Runtime = "osx-x64"
    } else {
        Write-Error "Unable to detect runtime. Please specify -Runtime parameter"
        exit 1
    }
}

Write-Host "Publishing CSharpMcp.Server for $Runtime..." -ForegroundColor Cyan

$publishArgs = @(
    "publish"
    "src/CSharpMcp.Server/CSharpMcp.Server.csproj"
    "-c", $Configuration
    "-o", $OutputDir
    "-r", $Runtime
    "--self-contained", "true"
    "-p:PublishSingleFile=true"
)

if (-not $NoTrim) {
    $publishArgs += "-p:PublishTrimmed=true"
    $publishArgs += "-p:TrimMode=partial"
}

if (-not $NoReadyToRun) {
    $publishArgs += "-p:PublishReadyToRun=true"
}

& dotnet $publishArgs

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "Publish successful!" -ForegroundColor Green

    # Get output file
    $exeName = if ($Runtime -like "win-*") { "CSharpMcp.Server.exe" } else { "CSharpMcp.Server" }
    $outputPath = Join-Path $OutputDir $exeName
    if (Test-Path $outputPath) {
        $fileSize = (Get-Item $outputPath).Length / 1MB
        Write-Host "  Output: $outputPath" -ForegroundColor Cyan
        Write-Host "  Size: $($fileSize.ToString('F2')) MB" -ForegroundColor Cyan
    }

    Write-Host ""
    Write-Host "To run:" -ForegroundColor Yellow
    if ($Runtime -like "win-*") {
        Write-Host "  .\$exeName" -ForegroundColor White
    } else {
        Write-Host "  ./$exeName" -ForegroundColor White
    }
} else {
    Write-Host ""
    Write-Host "Publish failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}
