<#
.SYNOPSIS
    Builds the Windows installer (DocAnon-<version>-win-x64-setup.exe).

.DESCRIPTION
    Three steps:

      1. Checks the models: models\ner (model.onnx, vocab.txt, ner-model.json)
         and models\tessdata (por.traineddata, eng.traineddata). When one of
         them is missing, tools\fetch-models.ps1 downloads and prepares it.
      2. dotnet publish  -> build\win-x64\       (desktop app and models)
                            build\win-x64\cli\   (command-line tool, which reads
                                                  the models from ..\models)
         both self-contained win-x64, so the target machine needs nothing else.
      3. ISCC DocAnon.iss -> dist\*-setup.exe

.PARAMETER Version
    Version stamped into the binaries, the installer and its file name. When it is
    omitted, the script reads <Version> from Directory.Build.props.

.PARAMETER SkipInstaller
    Stop after the publish step. Produces build\win-x64\ without Inno Setup.
#>
[CmdletBinding()]
param(
    [string]$Version = "",
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$scriptDir = Split-Path -Parent $PSCommandPath
$repoRoot  = Resolve-Path (Join-Path $scriptDir "..\..")
$payload   = Join-Path $repoRoot "build\win-x64"
$distDir   = Join-Path $repoRoot "dist"

function Write-Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }

if (-not $Version) {
    [xml]$props = Get-Content (Join-Path $repoRoot "Directory.Build.props") -Raw
    $Version = $props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1
    if (-not $Version) { throw "Directory.Build.props has no <Version>; pass -Version." }
}
Write-Host "    version: $Version" -ForegroundColor DarkGray

# ---------------------------------------------------------------- models ----
Write-Step "Checking the models"
$ner = Join-Path $repoRoot "models\ner"
$tess = Join-Path $repoRoot "models\tessdata"
$missing = @(@("model.onnx", "vocab.txt", "ner-model.json") | Where-Object { -not (Test-Path (Join-Path $ner $_)) }) +
           @(@("por.traineddata", "eng.traineddata") | Where-Object { -not (Test-Path (Join-Path $tess $_)) })
if ($missing.Count -gt 0) {
    Write-Step "Fetching the missing models ($($missing -join ', '))"
    & (Join-Path $repoRoot "tools\fetch-models.ps1")
}

# --------------------------------------------------------------- publish ----
Write-Step "Publishing DocAnon.App (self-contained win-x64) -> $payload"
if (Test-Path $payload) { Remove-Item $payload -Recurse -Force }
& dotnet publish (Join-Path $repoRoot "src\DocAnon.App\DocAnon.App.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:Version=$Version -p:DebugType=none `
    -o $payload --nologo
if ($LASTEXITCODE -ne 0) { throw "DocAnon.App publish failed" }

# DocAnon.exe and docanon.exe would collide on Windows, so the tool goes into cli\.
Write-Step "Publishing DocAnon.Cli -> $payload\cli"
& dotnet publish (Join-Path $repoRoot "src\DocAnon.Cli\DocAnon.Cli.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:Version=$Version -p:DebugType=none -p:DocAnonSkipModels=true `
    -o (Join-Path $payload "cli") --nologo
if ($LASTEXITCODE -ne 0) { throw "DocAnon.Cli publish failed" }

foreach ($required in @("DocAnon.exe", "cli\docanon.exe", "models\ner\model.onnx", "models\tessdata\por.traineddata", "x64\tesseract50.dll", "x64\leptonica-1.82.0.dll", "pdfium.dll", "onnxruntime.dll")) {
    if (-not (Test-Path (Join-Path $payload $required))) { throw "The payload is missing $required." }
}
$sizeMb = [Math]::Round(((Get-ChildItem $payload -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Host "    payload: $sizeMb MB" -ForegroundColor DarkGray

if ($SkipInstaller) { Write-Step "Done (installer skipped)"; return }

# ------------------------------------------------------------- installer ----
Write-Step "Compiling the installer (Inno Setup)"
$iscc = @(
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { $iscc = $cmd.Source }
}
if (-not $iscc) { throw "ISCC.exe not found. Install Inno Setup 6 (winget install JRSoftware.InnoSetup)." }

New-Item -ItemType Directory -Force -Path $distDir | Out-Null
$numericVersion = ($Version -split '[-+]')[0]
if ($numericVersion -notmatch '^\d+(\.\d+){0,3}$') { $numericVersion = "0.0.0" }

& $iscc "/DAppVersion=$Version" "/DAppVersionNumeric=$numericVersion" `
    "/DRepoRoot=$repoRoot" "/DPayloadDir=$payload" "/DOutputDir=$distDir" `
    (Join-Path $scriptDir "DocAnon.iss")
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit $LASTEXITCODE" }

Get-ChildItem $distDir -Filter "DocAnon-$Version-*-setup.exe" | ForEach-Object {
    Write-Host "==> $($_.FullName) ($([Math]::Round($_.Length / 1MB, 1)) MB)" -ForegroundColor Green
}
