# ONNX 모델 학습해서 들이기

2026-09-13. 앱은 `.onnx` 를 **읽기만** 한다. 학습은 앱 밖(파이썬)에서 한 번 돌리고 결과 파일만 가져온다.
왜 그렇게 나눴는지는 `몹-찾기-속도-설계.md` 4.5 에 있다.

## 0. 먼저 정할 것 - 어느 모델 가족인가

| 모델 | 라이선스 | 먹는 데이터 | 지금 앱이 출력을 풀 수 있나 |
|---|---|---|---|
| **D-FINE** (권장) | Apache-2.0 | COCO json | **된다** (DETR 계열) |
| RT-DETR (공식 저장소) | Apache-2.0 | COCO json | **된다** (DETR 계열) |
| YOLOX | Apache-2.0 | COCO json | 아직 - YOLO 디코더가 없다 |
| YOLO11 / YOLOv8 (Ultralytics) | **AGPL-3.0** | data.yaml | 아직 - YOLO 디코더가 없다 |

**팔 생각이 있으면 AGPL 은 피한다.** Ultralytics 는 그 도구로 학습한 <b>가중치까지</b> AGPL 로 본다.
지금 앱이 출력을 풀 줄 아는 것은 DETR 계열(`logits` · `pred_boxes`)뿐이다 - YOLO 계열로 가려면
`Vision/Inference/Onnx/` 에 YOLO 디코더(후보 거르기 + 겹침 제거)를 하나 더 만들어야 한다.

## 1. 데이터 내보내기 (앱 쪽)

```
Minguk.Tools.Tests.exe --export-dataset
```

데이터셋 폴더(`%AppData%\Minguk.Tools\Datasets\몹`)에 둘이 생긴다.

- `coco.json` - D-FINE · RT-DETR · YOLOX 가 먹는 모양. 사각형은 그림 픽셀 좌표다.
- `data.yaml` - Ultralytics 가 먹는 모양. 라벨 파일(`labels/*.txt`)은 이미 YOLO 형식이라 그대로 쓴다.

사진은 복사하지 않는다(97장 237MB). 두 파일이 원래 폴더를 가리킨다.
**학습·검증을 나누지 않았다** - 97장에서 10장을 떼면 검증 숫자가 너무 흔들린다. 대신 학습 뒤
`--detect-check` 로 **학습에 쓴 그림을 되찾는지** 본다(옛 모델과 같은 잣대, 지금 70%).

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

## 3. 들이기 (앱 쪽)

```
Minguk.Tools.Tests.exe --import-onnx --model=D:\...\best.onnx --size=640x640
```

- `detector.onnx` 로 복사하고 그 옆에 쪽지(`detector.onnx.json`)를 쓴다. **우리가 학습한 `detector.zip` 은 건드리지 않는다.**
- 쪽지가 이 모델을 가리키므로 앱은 다음 몹 찾기부터 새 모델로 돈다. libtorch 도, CPU 리드백도 필요 없다.

## 4. 견주기

```
Minguk.Tools.Tests.exe --detect-check          # 되찾기 % · 헛것 · 한 장 ms
Minguk.Tools.Tests.exe --onnx-texture --model=... --image=...   # GPU 길과 CPU 길이 같은 답인지
```

**받아들이는 문턱**: 되찾기 70% 이상(옛 모델과 같거나 낫게), 헛것은 한 자릿수, 한 장 30ms 이하.
하나라도 못 넘으면 되돌린다.

```
Minguk.Tools.Tests.exe --use-trained           # 옛 모델(detector.zip)로 돌아간다. onnx 파일은 그대로 남는다
```

## 5. 되찾기가 낮을 때

- **사진을 더 찍는다.** 97장은 적다. 봇이 멀리 있는 장면, 겹친 장면, 다른 지도를 더 담는다.
- 바퀴 수보다 사진이 먼저다 - 480x270 을 20바퀴·40바퀴로 돌려 봤지만 loss 가 0.42 에서 안 내려갔다(실측).
- 문턱(`--score=`)을 낮춰 보고, 헛것이 함께 늘면 모델이 아직 덜 배운 것이다.
