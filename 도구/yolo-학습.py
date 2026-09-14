"""
YOLO11 을 우리 데이터셋으로 학습해 ONNX 로 내보낸다. 보통은 혼자 부르지 않고 `도구/yolo-학습.ps1` 이 부른다.

    python 도구/yolo-학습.py --root <데이터셋> --work <시험 폴더> --runs <결과 폴더> [--model yolo11n] [--epochs 60] [--batch 8] [--device 0]

마지막 줄에 `ONNX <경로>` 를 찍는다 - ps1 이 그 경로를 받아 앱에 들인다.
학습 중에는 `BATCH …`(묶음마다)·`PREVIEW <그림>`(바퀴마다)을 찍는다 - 앱(YoloTrainer)이 진행 줄과 학습 묶음 그림을 띄운다(report_progress).

왜 이렇게 하나
--------------
- **내 PC 시험용이다.** Ultralytics 는 AGPL 이라 학습한 가중치까지 그 조건이다. 배포 모델은 D-FINE-N 이다
  (CLAUDE.md "모델 정책", docs/ONNX-모델-학습.md).
- **학습은 시험 폴더(--work)에서 한다.** images·labels 는 데이터셋을 가리키는 junction 이라 복사가 아니다(ps1 이 만든다).
  data.yaml·학습 결과가 데이터셋 폴더에 섞이지 않게 하려는 것이다. 다만 Ultralytics 는 junction 을 실제 경로로 풀어
  `labels.cache` 를 **데이터셋 폴더**(몹/labels.cache)에 쓴다(실측). 앱은 안 읽고, 라벨이 바뀌면 해시로 알아채 다시 만든다 - 해가 없다.
- **data.yaml 은 매번 classes.txt 로 새로 쓴다.** 몹을 더하거나 이름을 바꾼 뒤 옛 yaml 로 학습하면 번호가 어긋난다.
- **본문은 main 가드 안이다.** Windows 는 데이터 로더 작업자를 spawn 으로 띄워 이 파일을 다시 import 한다 -
  가드가 없으면 작업자마다 학습을 또 시작하려다 RuntimeError(bootstrapping)로 죽는다(실측).
- GTX 1060 3GB 에서 yolo11n 은 batch 8 이 1.3GB, 98장 60바퀴에 5분이었다. yolo11s 는 batch 4 로 10분이고 얻는 것이 없었다.
"""

import argparse
import os
import time
from pathlib import Path


def write_data_yaml(root: Path, work: Path) -> Path:
    names = [line.strip() for line in (root / "classes.txt").read_text(encoding="utf-8").splitlines() if line.strip()]

    if not names:
        raise SystemExit(f"classes.txt 에 몹이 없다: {root / 'classes.txt'}")

    work.mkdir(parents=True, exist_ok=True)

    # 사진은 데이터셋 자리를 바로 가리킨다. junction 을 거쳐도 Ultralytics 가 실제 경로로 풀어 같았다(실측) - yaml 파일만 work 에 둔다.
    lines = [
        "# 도구/yolo-학습.py 가 classes.txt 로 매번 새로 쓴다. 손으로 고치지 않는다.",
        f"path: {root.as_posix()}",
        "train: images",
        "val: images",  # 97장에서 떼면 검증 숫자가 너무 흔들린다 - 앱의 --detect-check 와 같은 잣대로 본다
        "",
        "names:",
        *[f"  {i}: {name}" for i, name in enumerate(names)],
        "",
    ]

    target = work / "data.yaml"
    target.write_text("\n".join(lines), encoding="utf-8")

    return target


def allow_capital_folders() -> None:
    """
    Ultralytics 가 라벨을 대소문자 안 가리고 찾게 한다 - 프로젝트 폴더가 `Images`·`Labels` 로 대문자로 시작하기 때문이다(2026-09-15).

    Ultralytics 는 사진 경로의 `\\images\\` 를 `\\labels\\` 로 **대소문자를 가려** 바꿔 라벨을 찾는다(data/utils.py img2label_paths).
    그런데 data.yaml 의 경로를 `resolve()` 로 실제 경로로 풀어 Windows 에서는 디스크의 이름(`Images`)이 돌아온다 - 그대로 두면
    라벨을 한 장도 못 찾고 "no labels found" 로 배경만 배운다. 데이터셋을 만드는 곳(dataset.py)이 가져다 쓴 이름을 바꿔 끼운다.
    """
    import re

    import ultralytics.data.dataset as dataset
    import ultralytics.data.utils as utils

    folder = re.compile(re.escape(os.sep) + "images" + re.escape(os.sep), re.IGNORECASE)

    def img2label_paths(img_paths, label_dir="labels", suffix=".txt"):
        result = []
        for path in img_paths:
            found = list(folder.finditer(path))
            if found:
                last = found[-1]
                path = path[: last.start()] + os.sep + label_dir + os.sep + path[last.end():]
            result.append(path.rsplit(".", 1)[0] + suffix)
        return result

    utils.img2label_paths = img2label_paths
    dataset.img2label_paths = img2label_paths


def use_class_colors(hexes: str | None) -> None:
    """
    묶음 그림의 상자 색을 라벨링 화면의 몹 색으로(사용자, 2026-09-15). `--colors FF0000,00A0FF` - 자리가 몹 번호.

    plot_images 는 모듈 전역 `colors`(Ultralytics 기본 20색)로 번호 색을 고른다 - 그 팔레트를 우리 색으로 갈아 끼운다.
    그림이 RGB(PIL)라 RGB 로 넣는다. 안 주면 Ultralytics 기본 색 그대로.
    """
    if not hexes:
        return

    import ultralytics.utils.plotting as plotting

    palette = [tuple(int(h[i:i + 2], 16) for i in (0, 2, 4)) for h in hexes.split(",") if len(h) == 6]
    if palette:
        plotting.colors.palette = palette
        plotting.colors.n = len(palette)


def report_progress(model, runs: Path) -> None:
    """
    앱이 학습을 따라가게 두 가지를 찍는다(사용자, 2026-09-15 - 라벨링 화면에서 학습하는 모습을 보고 싶다).

    - `BATCH <바퀴> <바퀴 수> <묶음> <묶음 수> <box loss>` - 묶음마다 한 줄. Ultralytics 진행 막대는 `\\r` 로 덮어써
      앱이 줄 단위로 읽으면 바퀴 끝에만 한 줄이 왔다(98장이면 5초에 한 번). loss 는 그 바퀴 안의 평균이다.
    - `PREVIEW <경로>` - 바퀴마다 첫 묶음을 라벨 상자와 함께 그린 그림(`학습-묶음.jpg`). YOLO 는 네 장을 이어 붙이고
      비틀어(mosaic) 한 묶음으로 배우므로 "지금 보는 그림 한 장" 이 없다 - 실제로 모델에 들어가는 묶음을 보여 준다.
      `plots=True` 로 켜면 결과 그래프까지 다 그려 느려져 필요한 것만 직접 그린다. 임시 파일에 쓰고 바꿔 끼워 앱이 반쯤 쓴 파일을 읽지 않게 한다.

    묶음 그림은 `on_train_batch_end` 에 오지 않는다 - 전처리(preprocess_batch)를 감싸 마지막 묶음을 붙들어 둔다.
    """
    from PIL import Image
    from ultralytics.utils.plotting import plot_images

    preview = runs / "학습-묶음.jpg"
    state = {"batch": None, "index": 0}

    def on_train_start(trainer):
        original = trainer.preprocess_batch

        def capture(batch):
            batch = original(batch)
            state["batch"] = batch
            return batch

        trainer.preprocess_batch = capture

    def on_train_epoch_start(trainer):
        state["index"] = 0

    def on_train_batch_end(trainer):
        state["index"] += 1
        count = len(trainer.train_loader)
        loss = float(next(iter(trainer.tloss.values()))) if trainer.tloss else 0.0
        print(f"BATCH {trainer.epoch + 1} {trainer.epochs} {state['index']} {count} {loss:.4f}", flush=True)

        batch = state["batch"]
        if state["index"] != 1 or batch is None:
            return

        try:
            # 넘긴 사전을 plot_images 가 고쳐 쓴다(텐서 → numpy) - 학습 중인 묶음을 건드리지 않게 얕은 복사본을 준다.
            grid = plot_images(labels=dict(batch), paths=batch["im_file"], names=trainer.data["names"],
                               max_size=1280, save=False, threaded=False)
            preview.parent.mkdir(parents=True, exist_ok=True)
            temporary = preview.with_suffix(".tmp.jpg")
            Image.fromarray(grid).save(temporary, quality=85)
            os.replace(temporary, preview)
            print(f"PREVIEW {preview}", flush=True)
        except Exception as error:  # 그림 하나 못 그렸다고 학습을 멈추지 않는다
            print(f"PREVIEW_FAILED {error}", flush=True)

    model.add_callback("on_train_start", on_train_start)
    model.add_callback("on_train_epoch_start", on_train_epoch_start)
    model.add_callback("on_train_batch_end", on_train_batch_end)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", required=True, help="데이터셋 폴더(images·labels·classes.txt)")
    parser.add_argument("--work", default=None, help="data.yaml 을 둘 폴더. 없으면 결과 폴더")
    parser.add_argument("--runs", required=True, help="학습 결과를 둘 폴더")
    parser.add_argument("--model", default="yolo11n")
    parser.add_argument("--epochs", type=int, default=60)
    parser.add_argument("--batch", type=int, default=8)
    parser.add_argument("--device", default="0")
    parser.add_argument("--colors", default=None, help="몹 번호 순서의 색 RRGGBB 를 쉼표로(묶음 그림 상자 색)")
    args = parser.parse_args()

    root, runs = Path(args.root), Path(args.runs)
    work = Path(args.work) if args.work else runs
    runs.mkdir(parents=True, exist_ok=True)

    data = write_data_yaml(root, work)

    # 사전학습 가중치(yolo11n.pt)는 지금 폴더로 내려받아진다 - 결과 폴더에 모아 둔다.
    os.chdir(runs)

    from ultralytics import YOLO  # 무겁다(torch). 인자 오류는 이것을 읽기 전에 끝낸다.

    allow_capital_folders()
    use_class_colors(args.colors)

    started = time.time()
    model = YOLO(f"{args.model}.pt")
    report_progress(model, runs / args.model)
    model.train(
        data=str(data), imgsz=640, epochs=args.epochs, batch=args.batch, device=args.device, workers=2,
        project=str(runs), name=args.model, exist_ok=True, plots=False, verbose=False)
    print(f"TRAIN_SECONDS {round(time.time() - started)}", flush=True)

    best = runs / args.model / "weights" / "best.pt"
    exported = YOLO(str(best)).export(format="onnx", imgsz=640, opset=17, simplify=True, dynamic=False)
    print(f"ONNX {exported}", flush=True)


if __name__ == "__main__":
    main()
