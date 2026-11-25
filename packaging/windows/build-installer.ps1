<#
.SYNOPSIS
    Builds the Windows installer (DocAnon-<version>-win-x64-setup.exe).

.DESCRIPTION
    Three steps:

      1. Checks the models: models\ner (model.onnx, vocab.txt, ner-model.json)
         and models\tessdata (por.traineddata, eng.traineddata). When the
         quantized model is missing but models\ner\source holds the Hugging Face
         export, tools\export_ner_model.py builds it.
      2. dotnet publish  -> build\win-x64\       (desktop app and models)
                            build\win-x64\cli\   (command-line tool, which reads
                                                  the models from ..\models)
         both self-contained win-x64, so the target machine needs nothing else.
      3. ISCC DocAnon.iss -> dist\*-setup.exe

.PARAMETER Version
    Version stamped into the binaries, the installer and its file name.

.PARAMETER SkipInstaller
    Stop after the publish step. Produces build\win-x64\ without Inno Setup.
#>
[CmdletBinding()]
param(
    [string]$Version = "0.1.0",
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$scriptDir = Split-Path -Parent $PSCommandPath
$repoRoot  = Resolve-Path (Join-Path $scriptDir "..\..")
$payload   = Join-Path $repoRoot "build\win-x64"
$distDir   = Join-Path $repoRoot "dist"

function Write-Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }

# ---------------------------------------------------------------- models ----
Write-Step "Checking the models"
$ner = Join-Path $repoRoot "models\ner"
$tess = Join-Path $repoRoot "models\tessdata"
if (-not (Test-Path (Join-Path $ner "model.onnx"))) {
    $source = Join-Path $ner "source"
    if (-not (Test-Path (Join-Path $source "model.onnx"))) {
        throw "models\ner\model.onnx is missing and models\ner\source has no Hugging Face export. See README, section 'Compilação'."
    }
    Write-Step "Quantizing the NER model"
    & python (Join-Path $repoRoot "tools\export_ner_model.py") --source $source --out $ner `
        --model-id Davlan/bert-base-multilingual-cased-ner-hrl --license afl-3.0
    if ($LASTEXITCODE -ne 0) { throw "export_ner_model.py failed" }
}
foreach ($f in @("por.traineddata", "eng.traineddata")) {
    if (-not (Test-Path (Join-Path $tess $f))) { throw "models\tessdata\$f is missing (tessdata_best from github.com/tesseract-ocr)." }
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
