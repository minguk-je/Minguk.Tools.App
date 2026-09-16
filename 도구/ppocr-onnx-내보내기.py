r"""
PP-OCRv5 모델을 ONNX 로 내보낸다. 한 번만 돌리고 결과물을 커밋한다.

왜 이 스크립트가 있나
  PaddleOCR 공식 저장소(HuggingFace)는 Paddle 형식(inference.json·inference.pdiparams)만 올려 둔다.
  우리는 ONNX Runtime + DirectML 로 돌리므로 한 번 변환해야 한다. 결과물(det.onnx·rec.onnx·rec-dict.txt)을
  Libs/PaddleOcr/ 에 커밋하므로 **앱을 쓰는 사람은 파이썬도 이 스크립트도 필요 없다.**

  모델을 올릴 때(PP-OCRv6 가 나오면)만 다시 돌린다.

쓰는 법
  py -3.12 -m venv venv
  venv\Scripts\pip install paddlepaddle==3.1.0 paddle2onnx==2.1.0 pyyaml packaging onnxruntime onnx_graphsurgeon
  venv\Scripts\python 도구\ppocr-onnx-내보내기.py

  **버전을 박아 둔 이유**(실측 2026-09-16)
    파이썬은 3.12 여야 한다 - paddlepaddle 이 3.14 휠을 아직 안 낸다.
    paddlepaddle 은 3.1.0 이어야 한다 - paddle2onnx 의 네이티브 확장이
    libpaddle.pyd·common.dll 에 직접 링크돼 있어서, 3.3.1 에서는 임포트가
    "지정된 프로시저를 찾을 수 없습니다" 로 터진다. protobuf 탓이 아니다.
    onnxruntime·onnx_graphsurgeon 은 변환 뒤 상수 접기(polygraphy)에 쓴다 - 없으면 조용히 건너뛴다.

라이선스
  PP-OCRv5 모델은 Apache-2.0 이다. YOLO(AGPL)와 달리 같이 배포해도 걸릴 것이 없다.
"""

import io
import shutil
import sys
import urllib.request
from pathlib import Path

# 내보낼 모델. det 는 글자 줄을 찾고, rec 는 찾은 줄을 읽는다.
# rec 를 한국어 판으로 고른 이유: 사전에 완성형 한글 11,172자 + ASCII 숫자·영문·기호가 모두 들어 있어
# 한 모델로 한글·영문·숫자를 같이 읽는다(자리마다 언어를 고를 필요가 없다).
MODELS = {
    "det": "PaddlePaddle/PP-OCRv5_mobile_det",
    "rec": "PaddlePaddle/korean_PP-OCRv5_mobile_rec",
}

# 받을 파일. inference.yml 은 변환에는 안 쓰지만 사전과 전처리 값이 여기 있다.
FILES = ["inference.json", "inference.pdiparams", "inference.yml"]

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "Libs" / "PaddleOcr"


def 받기(repo: str, 자리: Path) -> None:
    자리.mkdir(parents=True, exist_ok=True)

    for name in FILES:
        target = 자리 / name

        if target.exists():
            print(f"    {name} - 이미 있다")
            continue

        url = f"https://huggingface.co/{repo}/resolve/main/{name}"
        print(f"    {name} 받는 중...", end="", flush=True)
        urllib.request.urlretrieve(url, target)
        print(f" {target.stat().st_size / 1024 / 1024:.1f}MB")


def 변환(자리: Path, 저장: Path) -> None:
    # paddle2onnx 2.x 는 `python -m paddle2onnx` 진입점이 없다 - 함수로 부른다.
    import paddle2onnx

    저장.parent.mkdir(parents=True, exist_ok=True)

    paddle2onnx.export(
        model_filename=str(자리 / "inference.json"),
        params_filename=str(자리 / "inference.pdiparams"),
        save_file=str(저장),
        opset_version=14,
    )

    if not 저장.exists():
        raise SystemExit(f"변환 실패: {저장.name}")

    print(f"    → {저장.name}  {저장.stat().st_size / 1024 / 1024:.1f}MB")


def 사전뽑기(yml: Path, 저장: Path) -> None:
    """
    inference.yml 의 character_dict 를 한 줄에 한 글자씩 적는다.

    반드시 YAML 파서로 읽는다 - 줄을 손으로 자르면 인용된 항목(- '0' · - ',' · - ':')을 놓쳐
    숫자와 기호가 통째로 빠진다(실측 2026-09-16 - 처음에 이렇게 틀렸다).
    """
    import yaml

    with io.open(yml, encoding="utf-8") as f:
        설정 = yaml.safe_load(f)

    사전 = [str(c) for c in 설정["PostProcess"]["character_dict"]]

    with io.open(저장, "w", encoding="utf-8", newline="\n") as f:
        for c in 사전:
            f.write(c + "\n")

    한글 = sum(1 for c in 사전 if len(c) == 1 and 0xAC00 <= ord(c) <= 0xD7A3)
    숫자 = [c for c in "0123456789" if c in set(사전)]

    print(f"    → {저장.name}  {len(사전)}자 (완성형 한글 {한글} · 숫자 {''.join(숫자) or '없음!'})")

    if len(숫자) != 10:
        raise SystemExit("사전에 ASCII 숫자가 다 없다 - 파싱이 틀렸다.")


def main() -> None:
    # 영문 이름이어야 한다 - paddle2onnx 의 C++ 쪽이 한글 경로를 못 열어 "빈 입력" 으로 터진다(실측 2026-09-16).
    받은자리 = OUT / "_download"

    for 종류, repo in MODELS.items():
        print(f"[{종류}] {repo}")
        자리 = 받은자리 / 종류
        받기(repo, 자리)
        변환(자리, OUT / f"{종류}.onnx")

        if 종류 == "rec":
            사전뽑기(자리 / "inference.yml", OUT / "rec-dict.txt")

    # 받은 Paddle 원본은 커밋하지 않는다 - ONNX 와 사전만 있으면 된다.
    shutil.rmtree(받은자리, ignore_errors=True)

    print(f"\n끝. {OUT} 에 넣었다:")

    for f in sorted(OUT.iterdir()):
        print(f"  {f.name}  {f.stat().st_size / 1024 / 1024:.2f}MB")


if __name__ == "__main__":
    main()
