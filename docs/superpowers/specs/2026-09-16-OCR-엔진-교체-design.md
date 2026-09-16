# 글자 읽기 엔진을 PP-OCRv5 로 바꾼다

2026-09-16 · 설계

## 왜

지금은 Windows 내장 OCR(`Windows.Media.Ocr`) 하나다. 게임 글자를 그대로는 못 읽어서
**자리마다 손질을 사람이 골라 저장**하는 길로 메워 왔다 - 전처리 4종(그대로 키우기·밝기로 자동·
밝은 글자만·어두운 글자만) × 언어 2종(ko·en-US), 여기에 기울기 각도와 읽을 가로 범위까지.
`지금 읽기` 는 여덟 갈래를 다 읽어 보고 가장 긴 답을 골라 자리에 적는다.

이것은 게임이 하나 늘 때마다 사람이 다시 맞춰야 한다는 뜻이다. 사용자 판단(2026-09-16):

> 게임마다 전처리 할꺼야? 제대로 읽어주는 OCR 붙이자고

`IOcrEngine` 을 인터페이스로 둔 이유가 이것이다. 부르는 쪽을 안 고치고 엔진만 바꾼다.

## 무엇으로

**PP-OCRv5 mobile** (PaddleOCR), ONNX 로 내보내 기존 ONNX Runtime + DirectML 로 돌린다.

- `PP-OCRv5_mobile_det` - 글자 줄을 찾는다(DBNet)
- `korean_PP-OCRv5_mobile_rec` - 찾은 줄을 읽는다(CTC)

**라이선스 Apache-2.0** - YOLO(AGPL)와 달리 GitHub 에 올려도 걸릴 것이 없다.

**사전 11,945자** (`inference.yml` 의 `character_dict` 실측): 완성형 한글 11,172 + 조합용 자모 238 +
ASCII `0-9` + 영문 대소문자 + 기호 `.,:%/-+()[]|!?#`. **한 모델이 한글·영문·숫자를 같이 읽는다** -
자리마다 언어를 고르던 것이 통째로 필요 없어진다.

**정확도는 줄 단위 88%** (한 글자만 틀려도 그 줄은 실패). 100% 가 아니다 - 숫자 한 자리가 중요한
HUD 읽기는 여전히 가끔 틀린다. 그래도 지금보다는 확실히 낫고, 무엇보다 **손질을 안 고른다**.

### 안 고른 길

- **RapidOcrNet** - 가장 손쉽지만 `Microsoft.ML.OnnxRuntime` **CPU 1.29.0** 을 끌고 온다.
  우리 `...OnnxRuntime.DirectML 1.24.4` 와 같은 `onnxruntime.dll` 을 놓고 부딪친다. GPU 도 못 쓴다.
- **Sdcb.PaddleOCR** - 정확도는 검증됐지만 PaddleInference 네이티브(200MB+) + OpenCvSharp 가
  들어오고, 이미 실은 ONNX 스택과 중복된다.
- **Tesseract** - 게임 글꼴·외곽선 글자에 약하다. 결국 또 전처리를 요구한다.
- **방향 분류(cls)** - 게임 화면 글자는 뒤집히지 않는다. 뺀다.

## 짜임

```
Minguk.Tools/Vision/Ocr/
  IOcrEngine.cs          ← 안 바꾼다. 이번 작업의 핵심 이득이 여기 있다
  OcrEngineFactory.cs    ← PaddleOcrEngine 을 만든다
  Paddle/
    PaddleOcrEngine.cs   ← IOcrEngine 구현. det→rec 엮기 · 자물쇠 · 수명
    PaddleDetector.cs    ← DBNet 세션 · 레터박스 · 정규화
    DbPostProcess.cs     ← 확률맵 → 사각형 (순수 함수)
    PaddleRecognizer.cs  ← rec 세션 · 높이 48 정규화 · 폭 채우기
    CtcDecoder.cs        ← 탐욕 디코드 + 사전 (순수 함수)
    PaddleOcrModels.cs   ← 모델·사전 파일 자리
```

`DbPostProcess` 와 `CtcDecoder` 는 **ONNX 없이 배열만 넣어 검사한다** - 붙이기 전에 따로 맞춰 볼 수 있다.

## 모델 파일

`Libs/PaddleOcr/` 에 두고 `Content Include` 로 출력 `Models\Ocr\` 로 복사한다(DirectML DLL 과 같은
방식이되 출력 루트를 어지럽히지 않게 하위 폴더로).

| 파일 | 크기 |
|---|---|
| `det.onnx` | 약 4.7MB |
| `rec.onnx` | 약 16MB |
| `rec-dict.txt` | 11,945줄 |

공식 저장소(HuggingFace `PaddlePaddle/*`)는 Paddle 형식(`inference.json`·`inference.pdiparams`)만
올려 두어 **한 번 변환해야 한다**. `도구/ppocr-onnx-내보내기.py` 가 paddle2onnx 로 변환하고
`inference.yml` 에서 사전을 뽑는다. **결과물을 커밋하므로 쓰는 사람은 파이썬이 필요 없다.**

사전을 뽑을 때 **YAML 파서를 쓴다** - 줄을 손으로 자르면 인용된 항목(`- '0'` · `- ','` · `- ':'`)을
놓쳐 숫자와 기호가 통째로 빠진다(실측 2026-09-16 - 처음에 이렇게 틀렸다).

## DirectML 과 고정 크기

det·rec 둘 다 입력 크기가 가변인 모델이다. 그런데 **DirectML EP 는 입력 모양이 바뀔 때마다 커널을
다시 잡는다.** 자리마다 크기가 다르니 그대로 두면 읽을 때마다 수백 ms 씩 튀어 GPU 로 돌린 것이
오히려 느려진다. 그래서 **계단으로 묶는다**:

- **det** - 비율 유지로 줄여 `640×640` 한 칸에 레터박스(남는 곳 0). 자리는 대개 작아 한 칸이면 된다.
- **rec** - 높이 48 고정, 폭 `320`. 모자라면 오른쪽을 채우고, 넘치면 `640` 계단 하나 더. 둘뿐이다.

## 흐름

```
자리 조각(BitmapSource)
  → det 입력 [1,3,640,640]   RGB · (x/255 - mean)/std · 레터박스
                              mean .485/.456/.406 · std .229/.224/.225
  → 확률맵 [1,1,640,640]
  → 0.3 이진화 → 연결요소 → 축 사각형 → 1.5배 넓히기 → box_thresh 0.6 로 거르기
  → 레터박스 되돌려 조각 좌표로
  → 사각형마다 잘라 rec [1,3,48,320] → CTC 탐욕 디코드 → 글자
  → y 순으로 줄 묶기
  → OcrOutcome (Box 는 조각 안 0~1 비율 - 지금 계약 그대로)
```

**det 가 하나도 못 찾으면 자리 전체를 rec 에 한 번 넣는다**(안전망).

**축 사각형으로 바꿨다**(계획 단계) - 게임 UI 글자는 눕지 않고, 회전 사각형은 원근 변환이 따라온다.

## 잘 안 될 때

- 모델 파일이 없으면 팩터리가 **무엇이 없는지 한국어로** 말한다(지금 언어 팩 안내가 하던 자리).
- `TrainingActivity.IsBusy` 면 GPU 세션을 내려놓는다 - 몹 찾기가 이미 하는 규칙 그대로다
  (3GB 카드에서 캡처·DirectML·CUDA 학습이 겹쳐 GPU 가 리셋된 이력).
- `TrainingActivity.IsGpuLost` 면 **먼저 끄고 나서** 이유를 적는다.
- 동시 호출은 지금의 `SemaphoreSlim` 자물쇠를 그대로 쓴다(한 번에 하나).

## 걷어내는 것

```
Vision/Ocr/  WindowsOcrEngine.cs · IOcrPreprocessor.cs · OcrPreprocessorsImpl.cs
             AutoInkOcrPreprocessor.cs · HudInk.cs · NameplateInk.cs · OcrDeskew.cs
Tests/       OcrTuneCheck.cs (고를 손질이 없어진다)
```

`HudRegions.cs`·`NameplateRegion.cs` 는 **남긴다** - 저것은 "어디를" 계산하는 것이지 손질이 아니다
(`HudRegions` 는 스크립트의 `Ammo()`·`Health()`·`Ultimate()` 가 자리로 쓴다).

`Tests/PreprocessorReuseTests.cs` 도 **남긴다** - 이름이 비슷하지만 OCR 이 아니라 GPU 프레임
전처리기(`FramePreprocessor`) 검사다.

지우는 것이 아니라 **고쳐야 하는** 곳(손질을 부르고 있다):

| 파일 | 무엇 |
|---|---|
| `RecognizingCaptureViewModelBase.Ocr.cs:288` | 이름표를 `NameplateInk.Prepare` 로 손질해 넣는다 → 조각을 그대로 넣는다 |
| `LiveScriptApi.HudNumber` | `HudInk` 로 흰 글자만 남긴다 → 그대로 넣는다 |
| `Tests/NameplateCheck.cs` | 원본과 `NameplateInk` 를 나란히 찍는다 → 원본만 |
| `Tests/OcrTests.cs` | `TestNameplateInk()` → 새 엔진 검사로 바꾼다 |
| `Tests/OcrCropCheck.cs` | `--ink` 인자 → 뺀다 |

| 자리 | 지금 | 뒤 |
|---|---|---|
| `NamedRegion` | `Ink`·`Preprocessor`·`ShearDegrees`·`Language`·`Preprocess`·`PreprocessOptions` | 없음. `regions.json` 옛 키는 읽고 버린다 |
| `지금 읽기` | 여덟 갈래를 읽고 가장 긴 답 + 고른 손질을 저장 | 한 번 읽는다. 저장할 것이 없다 |
| `RecognizingCaptureViewModelBase` | `OcrLanguages`·`SelectedOcrLanguage`·`_engineByLanguage`·`OtherEngine`·`EngineFor` | 엔진 하나 |
| `LiveScriptApi` | `NumberOcr`·`LanguageOcr`·`OtherOcr`·`_languageOcr` + 빈 답 재시도 | 엔진 하나 |
| `ScriptStudioView.xaml` | 언어 콤보 + 그리드 `손질`·`언어`·`기울기`·`옛 전처리` 열 | 넷 다 뺀다 |

스크립트 API `읽기()`·`숫자읽기()`·`글자있나()` 의 **모양과 뜻은 안 바뀐다** - 기존 스크립트와
빌드된 `.mtsx` 가 그대로 돈다.

## 검증

1. **`--ocr-bench` (새로 만든다)** - 그림 묶음을 옛 Windows OCR 과 PP-OCRv5 로 같이 읽어 맞은 수를
   나란히 찍는다. **이것을 먼저 만들어 숫자를 본 다음에 걷어낸다.**
2. `--vision` 의 OCR 줄 갱신 (엔진 이름·속도·자리 읽기)
3. `--ocr-crop` 유지, 손질 인자만 뺀다
4. `--views` - 그리드 열이 빠지므로 화면 생성 확인
5. `--script-screen` - 영역 패널이 바뀌므로 정렬·PNG 확인

결과(2026-09-16, 오버워치 사격장 40장 × 3곳, 정답 119): PP-OCRv5 81 · 옛 길 67 · 옛 여덟 갈래 중 하나라도 75 - 관문 통과.
탄약 자리는 구분선 탓에 6/39(숫자 덩어리 17/39)로 여전히 약하다.

## 차례

```
1. 도구/ppocr-onnx-내보내기.py → det.onnx · rec.onnx · rec-dict.txt 를 만들어 커밋
2. CtcDecoder · DbPostProcess          (순수 함수, ONNX 없이 검사)
3. PaddleDetector · PaddleRecognizer · PaddleOcrEngine
4. --ocr-bench 로 옛것과 비교          ← 관문. 숫자를 보고 멈출 수 있다
5. 팩터리 교체 → 전처리·언어 걷어내기 → 화면 정리
6. 검사 전부 + docs/몹-검출.md 갱신
```

**4번이 관문이다.** 거기서 기대만큼 안 나오면 5번으로 넘어가지 않고 먼저 알린다.
