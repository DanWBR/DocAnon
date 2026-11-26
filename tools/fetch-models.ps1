<#
.SYNOPSIS
    Downloads the NER model and the Tesseract data into models\ and prepares the quantized NER model.

.DESCRIPTION
    The files are pinned to fixed upstream revisions and checked against their SHA-256, so every
    build ships the same models:

      models\ner\source\   Davlan/bert-base-multilingual-cased-ner-hrl (Hugging Face, AFL-3.0):
                           onnx/model.onnx, config.json, tokenizer_config.json, vocab.txt
      models\ner\          model.onnx quantized to int8 by tools\export_ner_model.py
      models\tessdata\     por.traineddata and eng.traineddata from tesseract-ocr/tessdata_best

    If models\ner\model.onnx already exists with the expected SHA-256, the script skips the
    709 MB download and the quantization. The quantization needs Python 3 with onnx 1.21.0 and
    onnxruntime 1.24.4, because other versions write a different file.

.PARAMETER Python
    Python executable used for the quantization.

.PARAMETER Force
    Downloads and quantizes again even when the files exist.
#>
[CmdletBinding()]
param(
    [string]$Python = "python",
    [switch]$Force
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$ProgressPreference = "SilentlyContinue"

$repoRoot = Resolve-Path (Join-Path (Split-Path -Parent $PSCommandPath) "..")
$nerDir = Join-Path $repoRoot "models\ner"
$sourceDir = Join-Path $nerDir "source"
$tessDir = Join-Path $repoRoot "models\tessdata"

$hfModel = "Davlan/bert-base-multilingual-cased-ner-hrl"
$hfRevision = "e756de7f7b8f64fea0c3d7c3872f1322fab747b1"
$tessRevision = "e12c65a915945e4c28e237a9b52bc4a8f39a0cec"

$nerFiles = @(
    @{ Remote = "onnx/model.onnx"; Local = "model.onnx"; Sha256 = "6c018415dc8129b358e9d629543c17481ad067ad02f9a6b8750473f161f9c5bd" },
    @{ Remote = "config.json"; Local = "config.json"; Sha256 = "ef78f60dc875b5e0f753fa3e2af22149fd2507c7b189d48a72b467e343e6ce07" },
    @{ Remote = "tokenizer_config.json"; Local = "tokenizer_config.json"; Sha256 = "91ac1b72b75113bf4d6ea69cf70932484fc4bc28394a6be4069f10f123293398" },
    @{ Remote = "vocab.txt"; Local = "vocab.txt"; Sha256 = "fe0fda7c425b48c516fc8f160d594c8022a0808447475c1a7c6d6479763f310c" }
)
# tools\export_ner_model.py output with onnx 1.21.0 and onnxruntime 1.24.4 (int8, reduce_range, per_channel).
$quantizedSha256 = "871cd401c08cc42c7ad346de4091ca875a1b049e6606e25375cbee6fb41c6678"
$tessFiles = @(
    @{ Name = "por.traineddata"; Sha256 = "711de9dbb8052067bd42f16b9119967f30bada80d57e2ef24f65d09f531adb04" },
    @{ Name = "eng.traineddata"; Sha256 = "8280aed0782fe27257a68ea10fe7ef324ca0f8d85bd2fd145d1c2b560bcb66ba" }
)

function Write-Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }

function Test-Sha256([string]$path, [string]$expected) {
    (Test-Path $path) -and ((Get-FileHash $path -Algorithm SHA256).Hash -eq $expected.ToUpperInvariant())
}

function Get-Verified([string]$url, [string]$path, [string]$expected) {
    if (-not $Force -and (Test-Sha256 $path $expected)) { return }
    Write-Host "    $url"
    $tmp = "$path.download"
    Invoke-WebRequest -Uri $url -OutFile $tmp -UseBasicParsing
    if (-not (Test-Sha256 $tmp $expected)) {
        Remove-Item $tmp -Force
        throw "SHA-256 mismatch for $url"
    }
    Move-Item $tmp $path -Force
}

# ------------------------------------------------------------------ NER ----
$quantized = Join-Path $nerDir "model.onnx"
if ($Force -or -not (Test-Sha256 $quantized $quantizedSha256)) {
    Write-Step "Downloading $hfModel"
    New-Item -ItemType Directory -Force -Path $sourceDir | Out-Null
    foreach ($f in $nerFiles) {
        Get-Verified "https://huggingface.co/$hfModel/resolve/$hfRevision/$($f.Remote)" (Join-Path $sourceDir $f.Local) $f.Sha256
    }

    Write-Step "Quantizing the NER model"
    & $Python -c "import onnx, onnxruntime" 2>$null
    if ($LASTEXITCODE -ne 0) { throw "Python needs the onnx and onnxruntime packages (pip install onnx onnxruntime)." }
    & $Python (Join-Path $repoRoot "tools\export_ner_model.py") --source $sourceDir --out $nerDir `
        --model-id $hfModel --license afl-3.0
    if ($LASTEXITCODE -ne 0) { throw "export_ner_model.py failed" }
    if (-not (Test-Sha256 $quantized $quantizedSha256)) {
        throw "The quantized model has an unexpected SHA-256. Use onnx 1.21.0 and onnxruntime 1.24.4."
    }
}
else {
    Write-Step "NER model already prepared ($quantized)"
}

# ------------------------------------------------------------ tessdata ----
Write-Step "Checking tessdata_best"
New-Item -ItemType Directory -Force -Path $tessDir | Out-Null
foreach ($f in $tessFiles) {
    Get-Verified "https://raw.githubusercontent.com/tesseract-ocr/tessdata_best/$tessRevision/$($f.Name)" (Join-Path $tessDir $f.Name) $f.Sha256
}

Write-Step "Models ready"
