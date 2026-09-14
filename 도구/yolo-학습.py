"""
YOLO11 을 우리 데이터셋으로 학습해 ONNX 로 내보낸다. 보통은 혼자 부르지 않고 `도구/yolo-학습.ps1` 이 부른다.

    python 도구/yolo-학습.py --root <데이터셋> --work <시험 폴더> --runs <결과 폴더> [--model yolo11n] [--epochs 60] [--batch 8] [--device 0]

마지막 줄에 `ONNX <경로>` 를 찍는다 - ps1 이 그 경로를 받아 앱에 들인다.

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


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", required=True, help="데이터셋 폴더(images·labels·classes.txt)")
    parser.add_argument("--work", default=None, help="data.yaml 을 둘 폴더. 없으면 결과 폴더")
    parser.add_argument("--runs", required=True, help="학습 결과를 둘 폴더")
    parser.add_argument("--model", default="yolo11n")
    parser.add_argument("--epochs", type=int, default=60)
    parser.add_argument("--batch", type=int, default=8)
    parser.add_argument("--device", default="0")
    args = parser.parse_args()

    root, runs = Path(args.root), Path(args.runs)
    work = Path(args.work) if args.work else runs
    runs.mkdir(parents=True, exist_ok=True)

    data = write_data_yaml(root, work)

    # 사전학습 가중치(yolo11n.pt)는 지금 폴더로 내려받아진다 - 결과 폴더에 모아 둔다.
    os.chdir(runs)

    from ultralytics import YOLO  # 무겁다(torch). 인자 오류는 이것을 읽기 전에 끝낸다.

    allow_capital_folders()

    started = time.time()
    YOLO(f"{args.model}.pt").train(
        data=str(data), imgsz=640, epochs=args.epochs, batch=args.batch, device=args.device, workers=2,
        project=str(runs), name=args.model, exist_ok=True, plots=False, verbose=False)
    print(f"TRAIN_SECONDS {round(time.time() - started)}", flush=True)

    best = runs / args.model / "weights" / "best.pt"
    exported = YOLO(str(best)).export(format="onnx", imgsz=640, opset=17, simplify=True, dynamic=False)
    print(f"ONNX {exported}", flush=True)


if __name__ == "__main__":
    main()
