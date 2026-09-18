<#
.SYNOPSIS
    검출 모델을 학습할 수 있게 파이썬 환경을 준비한다 - YOLO11 과 D-FINE 둘 다.

.DESCRIPTION
    앱(라벨링 화면 학습 버튼)이 쓰는 것과 같은 자리를 만든다. 새 PC 에서 한 번만 돌리면 된다.

    학습에 얽힌 것은 <Root> 한 폴더에 다 둔다(사용자 결정 2026-09-14) - 앱 설정 Vision.TrainingRoot 가 그 자리다.

      <Root>\yolo-venv\        YOLO 학습용 파이썬(torch cu126 + ultralytics)   약 2.5GB
      <Root>\dfine-venv\       D-FINE 학습용 파이썬(3.12 · torch cu124)        약 3GB
      <Root>\D-FINE\           D-FINE 저장소(학습 코드 + 우리 설정)
      <Root>\dfine_n_coco.pth  D-FINE 사전학습 가중치                          15MB
      <Root>uns\             학습 결과·로그

    받는 양이 크다. 회선이 종량제면 돌리지 말 것.

    **왜 cu126·cu124 를 못 박나** - GTX 1060 은 Pascal(sm_61)이라 더 새 CUDA 판에서 빠질 수 있다.
    끝에 `torch.cuda.get_arch_list()` 에 sm_61 이 있는지 본다.

    **D-FINE 설정은 저장소의 도구\dfine\ 에서 넣는다** - 검출 수·사진 자리는 앱이 학습 직전에 다시 쓰므로(DFineTrainer)
    여기서는 틀이 되는 두 파일만 제자리에 둔다.

.EXAMPLE
    .\도구\학습-환경-준비.ps1                 # 둘 다
    .\도구\학습-환경-준비.ps1 -What yolo      # YOLO 만
    .\도구\학습-환경-준비.ps1 -What dfine -Root "E:\학습"
#>
param(
    [ValidateSet("all", "yolo", "dfine")]
    [string] $What = "all",
    # 학습 폴더. 앱 설정 Vision.TrainingRoot 와 같아야 한다.
    [string] $Root = "D:\Minguk.Tools.학습"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$yoloVenv = Join-Path $Root "yolo-venv"
$legacyYolo = Join-Path $env:LOCALAPPDATA "Minguk.Tools\yolo-venv"

New-Item -ItemType Directory -Force $Root | Out-Null

function Step($text) { Write-Host ""; Write-Host "== $text" -ForegroundColor Cyan }
function Note($text) { Write-Host "   $text" -ForegroundColor DarkGray }

function Assert-Pascal($python) {
    $ok = & $python -c "import torch; print(torch.cuda.is_available() and 'sm_61' in torch.cuda.get_arch_list())"
    if ($ok.Trim() -ne "True") {
        Write-Host "!! torch 가 이 카드(sm_61)를 못 쓴다. 위 CUDA 판을 낮춰 다시 깔아야 한다." -ForegroundColor Yellow
    } else {
        Note "torch 가 이 카드를 쓴다(sm_61 있음)."
    }
}

# ── YOLO11 ──────────────────────────────────────────────────────────────────
if ($What -in @("all", "yolo")) {
    $python = Join-Path $yoloVenv "Scripts\python.exe"

    # 옛 자리(%LOCALAPPDATA%)에 있으면 옮긴다 - 2.5GB 를 다시 받을 이유가 없다. 폴더를 옮겨도 venv 안의 python.exe 는 그대로 돈다.
    if ((-not (Test-Path $python)) -and (Test-Path (Join-Path $legacyYolo "Scripts\python.exe"))) {
        Step "YOLO 환경을 학습 폴더로 옮긴다: $legacyYolo → $yoloVenv"
        Move-Item $legacyYolo $yoloVenv
    }

    if (Test-Path $python) {
        Step "YOLO 환경이 이미 있다: $yoloVenv"
    } else {
        Step "YOLO 환경을 만든다: $yoloVenv (약 2.5GB)"

        # Windows PowerShell 5.1 에는 삼항 연산자가 없다.
        $base = "python"
        if (Get-Command py -ErrorAction SilentlyContinue) { $base = "py" }

        & $base -m venv $yoloVenv
        & $python -m pip install --upgrade pip -q
        & $python -m pip install torch torchvision --index-url https://download.pytorch.org/whl/cu126
        & $python -m pip install ultralytics onnx onnxslim onnxruntime
    }

    Assert-Pascal $python
    Note "앱: 라벨링 화면에서 '쓰는 모델' 을 YOLO11n 으로 고르고 학습을 누르면 이 환경으로 돈다(98장 60바퀴에 약 5분)."
}

# ── D-FINE ──────────────────────────────────────────────────────────────────
if ($What -in @("all", "dfine")) {
    $dfine = Join-Path $Root "D-FINE"
    $venv = Join-Path $Root "dfine-venv"
    $python = Join-Path $venv "Scripts\python.exe"
    $weights = Join-Path $Root "dfine_n_coco.pth"
    $legacyDfine = Join-Path $Root ".venv"

    # 옛 이름(.venv)이면 새 이름으로 옮긴다 - 폴더만 보고 무엇인지 알게.
    if ((-not (Test-Path $python)) -and (Test-Path (Join-Path $legacyDfine "Scripts\python.exe"))) {
        Step "D-FINE 환경 이름을 바꾼다: .venv → dfine-venv"
        Move-Item $legacyDfine $venv
    }

    if (Test-Path (Join-Path $dfine "train.py")) {
        Step "D-FINE 저장소가 이미 있다: $dfine"
    } else {
        Step "D-FINE 저장소를 받는다: $dfine"
        if (-not (Get-Command git -ErrorAction SilentlyContinue)) { throw "git 이 없다. https://git-scm.com 에서 설치하고 다시 돌려라." }
        git clone --depth 1 https://github.com/Peterande/D-FINE $dfine
    }

    if (Test-Path $python) {
        Step "D-FINE 파이썬 환경이 이미 있다: $venv"
    } else {
        Step "D-FINE 파이썬 환경을 만든다: $venv (약 3GB)"
        # 파이썬 3.12 를 쓴다 - 학습 의존성(pycocotools 등)이 3.14 에 아직 안 맞는다. uv 가 그 판을 알아서 가져온다.
        if (-not (Get-Command uv -ErrorAction SilentlyContinue)) { throw "uv 가 없다. https://docs.astral.sh/uv 에서 설치하고 다시 돌려라." }

        uv venv --python 3.12 $venv
        uv pip install --python $python torch torchvision --index-url https://download.pytorch.org/whl/cu124
        uv pip install --python $python faster-coco-eval PyYAML tensorboard scipy calflops transformers loguru onnx onnxsim matplotlib pycocotools
    }

    Assert-Pascal $python

    if (Test-Path $weights) {
        Step "사전학습 가중치가 이미 있다: $weights"
    } else {
        Step "사전학습 가중치를 받는다: $weights (15MB)"
        # COCO 로 배운 것에서 이어 한다 - 98장으로 처음부터 배우게 하면 어림도 없다.
        Invoke-WebRequest -Uri "https://github.com/Peterande/storage/releases/download/dfinev1.0/dfine_n_coco.pth" -OutFile $weights
    }

    Step "우리 설정을 넣는다(검출 수·사진 자리는 앱이 학습 직전에 다시 쓴다)"
    Copy-Item (Join-Path $PSScriptRoot "dfine\mob_detection.yml") (Join-Path $dfine "configs\dataset\mob_detection.yml") -Force
    New-Item -ItemType Directory -Force (Join-Path $dfine "configs\dfine\custom") | Out-Null
    Copy-Item (Join-Path $PSScriptRoot "dfine\dfine_hgnetv2_n_mob.yml") (Join-Path $dfine "configs\dfine\custom\dfine_hgnetv2_n_mob.yml") -Force

    Note "앱 설정 Vision.TrainingRoot 가 이 폴더여야 한다: $Root"
    Note "앱: '쓰는 모델' 을 D-FINE-N 으로 고르고 학습을 누르면 여기로 돈다(98장 60바퀴에 약 1시간 45분)."
}

Write-Host ""
Write-Host "끝. 앱을 켜고 라벨링 화면에서 학습을 누르면 된다." -ForegroundColor Green
