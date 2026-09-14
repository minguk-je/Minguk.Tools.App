# ONNX 모델 학습해서 들이기

2026-09-13. 앱은 `.onnx` 를 **읽기만** 한다. 학습은 앱 밖(파이썬)에서 한 번 돌리고 결과 파일만 가져온다.
왜 그렇게 나눴는지는 `몹-찾기-속도-설계.md` 4.5 에 있다.

## 0. 먼저 정할 것 - 어느 모델 가족인가

| 모델 | 라이선스 | 먹는 데이터 | 지금 앱이 출력을 풀 수 있나 |
|---|---|---|---|
| **D-FINE** (권장) | Apache-2.0 | COCO json | **된다** (DETR 계열) |
| RT-DETR (공식 저장소) | Apache-2.0 | COCO json | **된다** (DETR 계열) |
| YOLOX | Apache-2.0 | COCO json | 아직 - 출력이 격자 날것이라 디코더가 따로 필요하다 |
| YOLO11 / YOLOv8 (Ultralytics) | **AGPL-3.0** | data.yaml | **된다** (2026-09-14, `YoloDecoder` - `output0 [1,4+몹수,후보수]`, 겹침 제거 0.45) - 시험용 |

**팔 생각이 있으면 AGPL 은 피한다.** Ultralytics 는 그 도구로 학습한 <b>가중치까지</b> AGPL 로 본다.
YOLO 디코더는 속도·인식률을 견주어 보려고 둔 것이다. 팔 물건에는 D-FINE·RT-DETR 을 쓴다.

### YOLO11 실측 (2026-09-14, GTX 1060 3GB, 98장 · 사각형 194개, 640, 60바퀴, 문턱 50%)

| 모델 | 학습 | 재현율 | 헛것 | 추론 한 장(DirectML) | ONNX |
|---|---|---|---|---|---|
| D-FINE-N (지금 쓰는 것) | 1시간 45분 | 193/194 (99%) | 0 | 28.5 ms | 15 MB |
| **YOLO11n** | **5분 2초** | **194/194 (100%)** | 1* | **10.9 ms** | 10 MB |
| YOLO11s | 10분 5초 | 194/194 (100%) | 1* | 23.5 ms | 38 MB |

\* 헛것이 아니다. `20260911-035740-748.png` 왼쪽 가장자리에 반쯤 걸친 봇이 실제로 있는데 라벨이 빠져 있었다(이름표·빨간 윤곽선이 보인다).
라벨을 더한 뒤 한 방 명령으로 다시 학습하니 **YOLO11n 195/195 · 헛것 0 · 10.3 ms** (학습 4분 56초).

- 재현율은 셋이 사실상 같다. 차이는 **속도(YOLO11n 이 D-FINE 의 2.6배)** 와 **학습 시간(21배 짧다)** 이다. 11s 는 n 보다 느리기만 하고 얻는 것이 없다.
- **DirectML 이 YOLO 는 맞게 계산한다** - 들일 때 GPU·CPU 견주기가 소수점까지 같았다. D-FINE 처럼 MatMul 을 고칠 필요가 없다.
- 라이선스(AGPL)는 그대로다. 앱에 기본으로 넣을지는 그것을 보고 정한다.

### YOLO11 시험하는 법 - 한 방 명령

```powershell
.\도구\yolo-학습.ps1                     # yolo11n · 60바퀴 → 앱이 쓰는 detector.onnx 에 들이고 재현율·속도까지
.\도구\yolo-학습.ps1 -Epochs 100
.\도구\yolo-학습.ps1 -Model yolo11s -Batch 4 -Target "$env:APPDATA\Minguk.Tools\Datasets\몹-yolo11s"   # 따로 견줄 때
```

환경이 없으면 처음 한 번 만든다(약 2.5GB). **앱이 켜져 있으면 하네스 빌드가 실행 파일 잠금으로 실패한다** - 앱을 닫고 돌린다.
실측(98장 · 195개): 학습·내보내기 5분 5초, 들이기·재현율·속도 1~2분. 배포 전에는 D-FINE-N 으로 되돌린다(CLAUDE.md "모델 정책").

아래는 그 명령이 속에서 하는 일이다.

1. 환경 - `%LOCALAPPDATA%\Minguk.Tools\yolo-venv` (Python 3.14, torch 2.14+cu126, ultralytics 8.4). **torch 는 cu126 판**을 받는다 -
   GTX 1060 은 Pascal(sm_61)이라 더 새 CUDA 판에서 빠질 수 있다. `torch.cuda.get_arch_list()` 에 `sm_61` 이 있는지 본다.
2. 데이터 - 쓰던 detector.onnx 를 덮지 않게 **시험 폴더**를 따로 둔다: `Datasets\몹-yolo` 에 `images`·`labels` 를 원래 폴더로 가리키는
   junction, `classes.txt` 복사, 그 폴더를 가리키는 `data.yaml`. Ultralytics 는 junction 을 실제 경로로 풀어 `labels.cache` 를
   **데이터셋 폴더**(몹\labels.cache)에 쓴다 - 앱은 안 읽고 라벨이 바뀌면 다시 만들어 해가 없다.
3. 학습·내보내기 - 파이썬에서 `YOLO('yolo11n.pt').train(data=..., imgsz=640, epochs=60, batch=8, device=0, workers=2)` 뒤
   `YOLO(best).export(format='onnx', imgsz=640, opset=17, simplify=True)`. 3GB 카드는 n 이 batch 8(1.3GB), s 는 batch 4.
   **Windows 는 본문을 `if __name__ == "__main__":` 안에 둔다** - 데이터 로더 작업자가 spawn 으로 파일을 다시 읽어, 가드가 없으면
   RuntimeError(bootstrapping)로 죽는다(실측).
4. 들이기 - `--import-onnx --model=best.onnx --size=640x640 --fit=비율 --root=<시험 폴더>` (Ultralytics 는 레터박스다. `--root=` 없으면 쓰던 모델을 덮는다).
5. 재기 - `--detect-check --root=<시험 폴더>` · `--onnx-bench --model=<시험 폴더>\detector.onnx`.

## 0.5 학습 환경 - 두 모델이 무엇을 쓰나

**앱에서 학습을 누르면 둘 다 앱 밖 파이썬을 띄운다.** 환경은 앱이 안 받는다(GB 단위라 버튼 한 번에 나가면 안 된다) -
새 PC 에서는 이것을 한 번 돌린다.

```powershell
.\도구\학습-환경-준비.ps1                       # 둘 다 (약 5.5GB)
.\도구\학습-환경-준비.ps1 -What yolo            # YOLO 만
.\도구\학습-환경-준비.ps1 -What dfine -Root "E:\학습"
```

**자리는 둘이다**(사용자 결정 2026-09-14) - 도구와 내 것을 나눈다. 설정 화면 "폴더" 에서 고르고, 기본값은 설치 루트 아래다.

```
<학습 경로>\            도구 - 지워도 스크립트로 다시 만든다 (Vision.TrainingRoot)
├─ yolo-venv\           YOLO 학습용 파이썬        약 2.5GB
├─ dfine-venv\          D-FINE 학습용 파이썬      약 3GB
├─ D-FINE\              D-FINE 저장소(학습 코드 + 우리 설정)
├─ dfine_n_coco.pth     D-FINE 사전학습 가중치    15MB
└─ runs\                학습 결과·로그

<프로젝트 경로>\        내 것 - 다시 만들 수 없다. 백업할 것은 이쪽 (Vision.ProjectsRoot)
├─ Datasets\<게임>\     사진 · 라벨 · classes.txt · 모델 · regions.json
└─ captures\            프레임 저장 그림
```

| | YOLO11n | D-FINE-N |
|---|---|---|
| 자리 | `<학습 폴더>\yolo-venv` | `<학습 폴더>\dfine-venv` + `D-FINE` |
| 파이썬 | 3.14 (venv) | **3.12** (uv) - 학습 의존성이 3.14 에 아직 안 맞는다 |
| torch | **cu126** + torchvision | **cu124** + torchvision |
| 그 밖 | ultralytics · onnx · onnxslim · onnxruntime | faster-coco-eval · PyYAML · tensorboard · scipy · calflops · transformers · loguru · onnx · onnxsim · matplotlib · pycocotools |
| 학습 코드 | ultralytics 패키지 | **저장소를 받는다** - `git clone Peterande/D-FINE` → `<Root>\D-FINE` |
| 사전학습 가중치 | `yolo11n.pt` 5MB (첫 학습에 알아서 받는다) | `dfine_n_coco.pth` 15MB (스크립트가 받는다) |
| 우리 설정 | 없음(앱이 data.yaml 을 매번 쓴다) | `도구\dfine\mob_detection.yml` · `dfine_hgnetv2_n_mob.yml` 을 저장소에 넣는다 |
| 받는 양 | 약 2.5GB | 약 3GB |
| 앱이 부르는 것 | `Tools\yolo-학습.py` | `train.py` → `export_onnx.py` → `Tools\onnx-DirectML-고치기.py` |
| 98장 60바퀴 | 약 5분 | 약 1시간 45분 |

- **torch 판을 못 박는 이유**: GTX 1060 은 Pascal(sm_61)이라 더 새 CUDA 판에서 빠질 수 있다. 스크립트가 끝에
  `torch.cuda.get_arch_list()` 에 sm_61 이 있는지 보고 없으면 크게 알린다.
- 폴더를 옮겼으면 앱 설정 **`Vision.TrainingRoot`** 한 줄만 바꾼다. 둘 다 그 아래를 본다.
- **가상환경은 복사해 옮기지 못한다** - `pyvenv.cfg` 에 그 PC 의 파이썬 경로가 박힌다(실측). 다른 PC 에서는 이 스크립트로 다시 만든다.
  PC 를 옮길 때 실제로 챙길 것은 데이터셋 폴더와 저장소뿐이고, 가중치 15MB 는 있으면 시간을 아끼는 정도다.
- 앱은 없는 것이 무엇인지(환경·저장소·설정·가중치) 상태 줄에 적고 이 스크립트를 가리킨다.

## 1. 데이터 내보내기 (앱 쪽)

```
Minguk.Tools.Tests.exe --export-dataset
```

데이터셋 폴더(`%AppData%\Minguk.Tools\Datasets\몹`)에 둘이 생긴다.

- `coco.json` - D-FINE · RT-DETR · YOLOX 가 먹는 모양. 사각형은 그림 픽셀 좌표다.
- `data.yaml` - Ultralytics 가 먹는 모양. 라벨 파일(`labels/*.txt`)은 이미 YOLO 형식이라 그대로 쓴다.

사진은 복사하지 않는다(97장 237MB). 두 파일이 원래 폴더를 가리킨다.
**학습·검증을 나누지 않았다** - 97장에서 10장을 떼면 검증 숫자가 너무 흔들린다. 대신 학습 뒤
`--detect-check` 로 **학습에 쓴 그림을 다시 찾는지** 본다(옛 모델과 같은 잣대, 지금 70%).

## 2. 학습 (파이썬, D-FINE 기준)

```bash
git clone https://github.com/Peterande/D-FINE
cd D-FINE
pip install -r requirements.txt

# 설정 하나를 복사해 데이터 자리를 우리 것으로 바꾼다:
#   train_dataloader.dataset.img_folder: .../Datasets/몹/images
#   train_dataloader.dataset.ann_file   : .../Datasets/몹/coco.json
#   val_dataloader   도 같은 것을 가리키게 (우리는 안 나눴다)
#   num_classes: 1        ← classes.txt 의 줄 수

python train.py -c configs/dfine/dfine_hgnetv2_n_coco.yml --use-amp
python tools/deployment/export_onnx.py -c configs/dfine/dfine_hgnetv2_n_coco.yml -r output/best.pth
```

- **사전학습 가중치에서 시작한다**(COCO). 97장으로 처음부터 배우게 하면 어림도 없다.
- 입력 크기는 640x640 이 기본이다. 바꾸면 들일 때 `--size` 도 같이 바꾼다.
- GPU 가 있어야 한다. 4070 노트북에서 97장·수십 바퀴면 몇십 분 수준이다(우리 ML.NET 학습은 46분이었다).

## 2.5 DirectML 이 먹을 수 있게 고치기 (**빼먹으면 한 마리도 못 찾는다**)

```bash
python 도구/onnx-DirectML-고치기.py output/mob_n/best_stg2.onnx
# → best_stg2-dml.onnx
```

DirectML 은 `MatMul` 의 오른쪽이 **1차원 벡터**일 때 그 벡터를 무시하고 행 합계를 돌려준다(실측).
D-FINE 디코더에 이 모양이 세 군데 있어서, 잘 배운 모델이 **점수 0.9 → 0.06** 이 되고 재현율이 0% 가 된다.
터지지도 느려지지도 않고 **답만 조용히 틀린다** - 헛것도 0개라 "덜 배웠나" 로 보인다.
스크립트는 그 벡터를 [N,1] 행렬로 바꿔 곱하고 축을 다시 없앤다(답은 CPU 와 소수점까지 같다).

들일 때 앱이 GPU·CPU 를 견줘 보고 어긋나면 막는다. 혼자 확인하려면:

```
Minguk.Tools.Tests.exe --onnx-agree --model=... --image=...
Minguk.Tools.Tests.exe --onnx-raw   --model=... --image=... [--cpu]   # 날것 출력
```

## 3. 들이기 (앱 쪽)

```
Minguk.Tools.Tests.exe --import-onnx --model=D:\...\best_stg2-dml.onnx --size=640x640
```

- `detector.onnx` 로 복사하고 그 옆에 쪽지(`detector.onnx.json`)를 쓴다. **우리가 학습한 `detector.zip` 은 건드리지 않는다.**
- `--fit=` 은 **그림을 어떻게 넣어 학습했는지**다. 기본은 `늘리기` - D-FINE·RT-DETR 공식 설정의 변환이
  `Resize [640,640]` 하나뿐이라 비율을 안 지킨다. YOLO 계열로 학습했으면 `--fit=비율`.
  여기가 학습과 다르면 역시 한 마리도 못 찾는다.
- 쪽지가 이 모델을 가리키므로 앱은 다음 몹 찾기부터 새 모델로 돈다. libtorch 도, CPU 리드백도 필요 없다.

## 3.5 다른 PC(노트북)에서 학습하기

가상환경(.venv)은 **복사하지 않는다** - 안에 이 PC 경로가 박혀 있어 옮기면 깨진다. 다시 만드는 데 몇 분이면 된다.

옮길 것은 셋뿐이다.

| 옮길 것 | 크기 | 비고 |
|---|---|---|
| `D-FINE` 저장소(우리 설정 포함) | 수십 MB | `configs/dataset/mob_detection.yml` · `configs/dfine/custom/dfine_hgnetv2_n_mob.yml` |
| `dfine_n_coco.pth` | 15 MB | 사전학습 가중치 |
| 데이터셋 폴더 | 240 MB | 사진·라벨. 이미 옮겨 뒀으면 그것을 쓴다 |

노트북에서 할 일:

```bash
uv venv --python 3.12 .venv
uv pip install --python .venv/Scripts/python.exe torch torchvision --index-url https://download.pytorch.org/whl/cu124
uv pip install --python .venv/Scripts/python.exe faster-coco-eval PyYAML tensorboard scipy calflops transformers loguru onnx onnxsim matplotlib pycocotools

# 앱에서 데이터셋 폴더를 고른 뒤 다시 내보낸다(coco.json 의 자리가 그 PC 것이어야 한다)
Minguk.Tools.Tests.exe --export-dataset

# mob_detection.yml 의 img_folder · ann_file 을 그 PC 경로로 고친다
# VRAM 이 8GB 면 배치를 올린다: total_batch_size: 16  (1060 3GB 에서는 4였다)

set PYTHONUTF8=1
.venv\Scripts\python.exe train.py -c configs/dfine/custom/dfine_hgnetv2_n_mob.yml --use-amp --seed=0 -t dfine_n_coco.pth
```

- **`PYTHONUTF8=1` 이 필요하다** - 한국어 윈도우의 기본 인코딩(cp949)으로 설정 파일을 읽다 터진다(실측).
- 돌아올 때는 **내보낸 `.onnx` 하나만**(15MB) 가져오면 된다. `.pth`(60MB)는 다시 학습할 때만 쓴다.

## 4. 견주기

```
Minguk.Tools.Tests.exe --detect-check          # 재현율 % · 헛것 · 한 장 ms
Minguk.Tools.Tests.exe --onnx-texture --model=... --image=...   # GPU 길과 CPU 길이 같은 답인지
```

**받아들이는 문턱**: 재현율 70% 이상(옛 모델과 같거나 낫게), 헛것은 한 자릿수, 한 장 30ms 이하.
하나라도 못 넘으면 되돌린다.

**2026-09-13 실측 (GTX 1060 3GB)** - D-FINE-N 97장 60바퀴(1시간 45분), 640x640 늘리기:

| | 옛 모델(우리 학습, 640x360) | 새 모델(D-FINE-N) |
|---|---|---|
| 재현율 | 70% | **99%** (192개 중 191개) |
| 헛것 | 5개 | **0개** |
| 추론 한 장 | 606 ms | **32 ms** |
| 모델 크기 | 83 MB | 15 MB |
| 필요한 것 | libtorch 4.3GB | 없음(ONNX Runtime 뿐) |

```
Minguk.Tools.Tests.exe --use-trained           # 옛 모델(detector.zip)로 돌아간다. onnx 파일은 그대로 남는다
```

## 5. 재현율이 낮을 때

- **사진을 더 찍는다.** 97장은 적다. 봇이 멀리 있는 장면, 겹친 장면, 다른 지도를 더 담는다.
- 바퀴 수보다 사진이 먼저다 - 480x270 을 20바퀴·40바퀴로 돌려 봤지만 loss 가 0.42 에서 안 내려갔다(실측).
- 문턱(`--score=`)을 낮춰 보고, 헛것이 함께 늘면 모델이 아직 덜 배운 것이다.
