<#
.SYNOPSIS
    YOLO11 을 학습해 앱에 들이고 되찾기까지 잰다 - 내 PC 시험용 한 방 명령.

.DESCRIPTION
    1) 파이썬 환경이 없으면 만든다(%LOCALAPPDATA%\Minguk.Tools\yolo-venv, torch cu126 + ultralytics, 처음 한 번 약 2.5GB)
    2) 시험 폴더(Datasets\<이름>-yolo)에 images·labels junction 을 둔다(복사 아님)
    3) 도구\yolo-학습.py 로 학습 → ONNX 내보내기
    4) 하네스로 들이기(--import-onnx --fit=비율) → 되찾기(--detect-check) → 속도(--onnx-bench)

    배포 모델은 D-FINE-N 이다(YOLO 는 AGPL). 배포 전에 되돌리는 법은 CLAUDE.md "모델 정책".
    -Target 을 안 주면 데이터셋 폴더의 detector.onnx(앱이 쓰는 자리)에 들인다. 옆의 detector.dfine-n.onnx 는 안 건드린다.

.EXAMPLE
    .\도구\yolo-학습.ps1
    .\도구\yolo-학습.ps1 -Epochs 100
    .\도구\yolo-학습.ps1 -Model yolo11s -Batch 4 -Target "$env:APPDATA\Minguk.Tools\Datasets\몹-yolo11s"
#>
param(
    [string] $Model = "yolo11n",
    [int] $Epochs = 60,
    [int] $Batch = 8,
    [string] $Device = "0",
    # 사진·라벨이 있는 데이터셋. 비우면 앱 기본 자리.
    [string] $Root = (Join-Path $env:APPDATA "Minguk.Tools\Datasets\몹"),
    # 들일 곳. 비우면 $Root(앱이 쓰는 모델을 바꾼다). 따로 견주려면 다른 폴더를 준다 - junction 은 알아서 만든다.
    [string] $Target = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$venv = Join-Path $env:LOCALAPPDATA "Minguk.Tools\yolo-venv"
$python = Join-Path $venv "Scripts\python.exe"
$runs = Join-Path $env:APPDATA "Minguk.Tools\yolo-runs"
$work = "$Root-yolo"
if ([string]::IsNullOrEmpty($Target)) { $Target = $Root }

function Step($text) { Write-Host ""; Write-Host "== $text" -ForegroundColor Cyan }

function Ensure-Junctions($folder) {
    New-Item -ItemType Directory -Force $folder | Out-Null
    foreach ($name in "images", "labels") {
        $link = Join-Path $folder $name
        if (-not (Test-Path $link)) { New-Item -ItemType Junction -Path $link -Target (Join-Path $Root $name) | Out-Null }
    }
    Copy-Item (Join-Path $Root "classes.txt") $folder -Force
}

# ── 1) 파이썬 환경 ──────────────────────────────────────────────────────────
if (-not (Test-Path $python)) {
    Step "파이썬 환경을 만든다: $venv (처음 한 번, 약 2.5GB)"
    # Windows PowerShell 5.1 에는 삼항 연산자가 없다.
    $base = "python"
    if (Get-Command py -ErrorAction SilentlyContinue) { $base = "py" }
    & $base -m venv $venv
    & $python -m pip install --upgrade pip -q
    # cu126 판이어야 한다 - GTX 1060(Pascal, sm_61)은 더 새 CUDA 판에서 빠질 수 있다.
    & $python -m pip install torch torchvision --index-url https://download.pytorch.org/whl/cu126
    & $python -m pip install ultralytics onnx onnxslim onnxruntime
}

$arch = & $python -c "import torch; print(torch.cuda.is_available() and 'sm_61' in torch.cuda.get_arch_list())"
if ($arch.Trim() -ne "True") { throw "torch 가 이 카드(sm_61)를 못 쓴다. yolo-venv 를 지우고 cu126 판으로 다시 만들어라." }

# ── 2) 시험 폴더 ────────────────────────────────────────────────────────────
Step "시험 폴더: $work (images·labels 는 $Root 를 가리킨다)"
Ensure-Junctions $work
if ($Target -ne $Root) { Ensure-Junctions $Target }

# ── 3) 학습 · 내보내기 ──────────────────────────────────────────────────────
Step "학습: $Model · $Epochs 바퀴 · batch $Batch · GPU $Device"
$log = Join-Path $runs "$Model.log"
New-Item -ItemType Directory -Force $runs | Out-Null
$watch = [Diagnostics.Stopwatch]::StartNew()

# 진행 막대가 \r 로 줄을 덮어써 로그가 수 MB 가 된다 - 파일에는 다 남기고 화면에는 바퀴 끝과 결과만 보인다.
# 5.1 은 2>&1 로 받은 stderr 줄을 ErrorRecord 로 감싸 Stop 이면 경고 한 줄에 멈춘다 - 이 구간만 Continue 로 두고 글로 바꾼다.
$ErrorActionPreference = "Continue"
& $python -X utf8 -u (Join-Path $PSScriptRoot "yolo-학습.py") --root $Root --work $work --runs $runs `
    --model $Model --epochs $Epochs --batch $Batch --device $Device 2>&1 |
    ForEach-Object { "$_" } |
    Tee-Object -FilePath $log |
    ForEach-Object { ($_ -split "`r")[-1] } |
    Where-Object { $_ -match "^\s+all\s|TRAIN_SECONDS|^ONNX |Traceback|Error" }
$trainExit = $LASTEXITCODE
$ErrorActionPreference = "Stop"

if ($trainExit -ne 0) { throw "학습이 실패했다. 로그: $log" }

$onnx = (Select-String -Path $log -Pattern "^ONNX (.+)$" | Select-Object -Last 1).Matches.Groups[1].Value.Trim()
if (-not (Test-Path $onnx)) { throw "내보낸 ONNX 를 못 찾았다. 로그: $log" }
Write-Host ("학습·내보내기 {0:mm\:ss}" -f $watch.Elapsed)

# ── 4) 들이기 · 재기 ────────────────────────────────────────────────────────
$harness = Join-Path $repo "Minguk.Tools.Tests"

# 하네스를 먼저 조용히 빌드한다 - dotnet run 이 빌드하면 경고 수십 줄이 결과를 덮는다. 오류만 보인다.
Step "하네스 빌드"
$ErrorActionPreference = "Continue"
dotnet build $harness -c Debug -nologo -v q 2>&1 | ForEach-Object { "$_" } | Where-Object { $_ -match " error |오류 [1-9]" }
$buildExit = $LASTEXITCODE
$ErrorActionPreference = "Stop"
if ($buildExit -ne 0) { throw "하네스 빌드가 실패했다. 앱이 켜져 있으면 실행 파일이 잠겨 못 쓴다 - 앱을 닫고 다시 돌려라." }

Step "들이기: $Target (비율 지켜 여백 - Ultralytics 는 레터박스다)"
dotnet run --project $harness -c Debug --no-build -- --import-onnx "--model=$onnx" --size=640x640 --fit=비율 "--root=$Target" "--name=$("YOLO" + $Model.Substring(4))"
if ($LASTEXITCODE -ne 0) { throw "들이지 못했다(GPU·CPU 답이 다르면 여기서 멈춘다)." }

Step "되찾기 (학습 그림 기준, 문턱 50%)"
dotnet run --project $harness -c Debug --no-build -- --detect-check "--root=$Target" --score=0.5 |
    Where-Object { $_ -match "^== |헛것 [1-9]|FAIL|모델:" }

Step "속도"
dotnet run --project $harness -c Debug --no-build -- --onnx-bench "--model=$(Join-Path $Target 'detector.onnx')" --size=640x640 --runs=60 |
    Where-Object { $_ -match "추론 한 장" }

Write-Host ""
Write-Host "끝. 라벨링 화면 '쓰는 모델' 콤보에 YOLO11n 이 들어갔다. 넘겨줄 때는 그 콤보에서 D-FINE-N 을 고른다(YOLO 는 AGPL)." -ForegroundColor Yellow
