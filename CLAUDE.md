**답은 모두 한국어로 쓴다**(사용자). 결과 보고·커밋 보고·한 줄짜리 진행 상황까지 예외 없다. 코드 이름·명령어·파일 이름만 원문으로 둔다.
긴 대화 끝에서 결과를 정리할 때 영어로 새는 일이 여러 번 났다 - 보내기 전에 언어를 한 번 더 본다.

DevExpress WPF 컨트롤, WPF 개발자

## 이 프로젝트

TamsTools 의 셸 구조(MainWindow / MainView / MainViewModel / MainMenu)와 기준 스타일(Minguk.Base)을 가져온 게임 자동화 도구.
화면캡처 → 라벨링 → 학습(ONNX) → 스크립트(검출·조준·글자 읽기) → 빌드(.mtsx) → 플레이.

자세한 규칙·실측은 docs 에 나눠 둔다 - 그 영역을 고칠 때 먼저 읽는다.

| 문서 | 무엇 |
|---|---|
| `docs/프로젝트-설계.md` | 솔루션·프로젝트·작업공간, 폴더 모양, 솔루션 탭·셸 |
| `docs/검출.md` | 라벨 형식, ONNX 모델·DirectML 함정, 학습, 실시간 검출, OCR·이름 붙인 자리, 라벨링 화면 |
| `docs/실시간-스크립트.md` | 실시간 API·안전장치, 목표·조준, 입력 경로, 전역 단축키, 디버그 |
| `docs/스크립트-프로젝트-설계.md` · `docs/스크립트-설계.md` | 스크립트 화면(VS 모양)·프로젝트 파일 |
| `docs/솔루션-설정.md` | 설정 탭(사용자가 칸을 놓아 만드는 설정 화면)·솔루션/프로젝트 두 층·스크립트 `설정()` |
| `docs/ONNX-모델-학습.md` · `docs/검출-속도-설계.md` | 학습 절차 · 추론 속도 |

## 짜임

```
Minguk Tools (틀)
├ 자동화                      ← 그룹
│  ├ 환경설정                 ← 모듈(Minguk.Tools.Training). 작업공간·학습 경로를 정한다
│  ├ 솔루션                   ← AutomationMainView. 솔루션이 없으면 시작 창
│  │    위 : 작업공간 → [솔루션 ▾] → [프로젝트 ▾]  [새 프로젝트][새 공유 프로젝트][새 솔루션]
│  │    아래 탭 : 화면캡처 · 라벨링 · 스크립트 · 설정   ← 고른 프로젝트를 따라간다
│  └ 플레이                   ← bin 의 완성품(.mtsx)을 돌린다
└ 입력 테스트                 ← 입력 경로 점검(계획 모드)
제목 표시줄 : 최상위 · 테마      창 제목 : Minguk Tools (프로젝트 이름 안 넣음)
```

- 게임 하나 = **솔루션**(`.mtsln`), 모드·스테이지·런 = **프로젝트**(`.mtsproj`). VS 2026 과 같은 말이다. 사진·라벨·모델·영역·스크립트는 **프로젝트마다 제 것**.
- 솔루션들이 든 폴더를 화면에서 **작업공간** 이라 부른다(설정 키 `Vision.ProjectsRoot`·`ProjectPaths` 이름은 저장값 때문에 그대로). 작업공간은 솔루션 폴더의 **위** 폴더다.
- **화면들이 고른 프로젝트를 본다** - 기준은 `SolutionWorkspace.StartupDirectory` 하나: 데이터셋·모델·영역(`LabelDataset.ConfiguredRoot`), 스크립트 자리(`ScriptFiles.DefaultDirectory`),
  프레임 저장(`ProjectPaths.Captures`), 스크립트 화면이 여는 `.mtsproj`. 솔루션이 없을 때만 옛 자리(`LegacyRoot`)를 본다.
  이름 주의: `Minguk.Tools.Input.Scripting` 안에서 `Projects.` 는 `Input.Scripting.Projects` 로 잡혀 `global::Minguk.Tools.Projects` 로 적는다.
- 자세한 것(솔루션 탭 문서·프로젝트 바꾸기·공유 프로젝트·솔루션 탐색기)은 `docs/프로젝트-설계.md`.
- **설정 탭**(사용자, 2026-09-16): 사람이 칸(글자·숫자·체크·콤보·슬라이더·구역·목록)을 놓아 설정 화면을 만들고 스크립트가 `설정<int>("이름")`·`설정저장` 으로 읽고 쓴다.
  두 층 - 솔루션 폴더·프로젝트 폴더의 `settings.form.json`(양식)·`settings.values.json`(값), 찾는 순서 프로젝트 값 → 솔루션 값 → 기본값. 이름 하나에 정의 하나(만들 때 막고, 이미 겹치면 프로젝트가 이김).
  폴더마다 앱에 한 벌(`SettingsLayer.For`)이라 도는 스크립트와 설정 탭이 곧바로 서로 본다. 자세한 것은 `docs/솔루션-설정.md`.

## 화면 하나 추가하는 법

**새 기능은 모듈 프로젝트로 만든다**(아래). 셸 안에 화면을 더할 때는:

1. `Views/XxxView.xaml` + `.xaml.cs` (UserControl)
2. `ViewModels/XxxViewModel.cs` — `DocumentViewModelBase` 상속, `Create()` 팩터리, 생성자에서 `Caption`/`CaptionImage`(탭 캡션 = 메뉴 이름)
3. `App.xaml.cs` 의 `builder.Services.AddTransient<XxxView>()` - 빠뜨리면 메뉴는 보이는데 탭이 비어서 열린다(`MainViewLocator` 가 DI 에서 꺼낸다)
4. `Source/MainMenu.cs` 에 `MenuItemModel.Create(...)`(`class_nm` = View 전체 타입 이름). 솔루션 탭 아래 화면이면 `Source/AutomationScreens.cs`.

대시보드(`DashboardView`)는 메뉴에서 빼고 ViewModel 최소 예시로 남겨 두었다.

## 기능 모듈 - 셸은 뼈대, 기능은 프로젝트

```
Minguk.Tools.Core        ← 셸과 모듈이 함께 보는 가운데 조각 (기능 코드는 넣지 않는다. LabelBox 도 여기 - OCR 이 쓴다)
   ↑              ↑
Minguk.Tools    Minguk.Tools.Training     ← 모듈. 화면 + 메뉴를 들고 온다
   ↑
   ├ Minguk.Tools.Input     ← 입력 어댑터·전역 단축키·대상 창·계획 순서·한글 자판 + Native\x64\interception.dll (아무것도 참조 안 함)
   ├ Minguk.Tools.Capture   ← WGC·영상 세션·공유 허브·녹화·미리보기 입력 좌표 (Base·Input 참조)
   └ Minguk.Tools.Ocr       ← IOcrEngine·PP-OCRv5·Windows OCR·팩터리·OnnxDmlEngine·TensorSpec + Models\Ocr (Core 참조)
```

- **바탕 조각**(2026-09-18, 사용자 "OCR·캡처·입력을 별도의 프로젝트로"): 화면이 없는 라이브러리다. `RootNamespace` 가 `Minguk.Tools` 라 **네임스페이스는 옮기기 전 그대로**(`Minguk.Tools.Capture`·`Minguk.Tools.Input`·`Minguk.Tools.Vision.Ocr`·`Minguk.Tools.Inference`) -
  using 은 안 고쳤다. XAML 에서 그 네임스페이스를 쓰면 `;assembly=Minguk.Tools.Input` 처럼 어셈블리를 적어야 한다(`InputAutomationView`).
  셸에 남은 것: 스크립트(`Input\Scripting` - Vision·ViewModel 을 다 쓴다), 자리 규칙(`Vision\Ocr\HudRegions`·`NameplateRegion`), 검출(`Vision\Inference`).
  바탕 조각은 셸·Vision·학습을 모른다 - "학습 중이면 OCR 을 CPU 로" 같은 판단은 화면(`RecognizingCaptureViewModelBase`)이 하고 종류만 넘긴다.
  네이티브·모델 파일은 그 조각의 csproj 가 `Content` 로 나른다(ProjectReference 를 타고 셸·하네스 출력에 온다).

- Core 에 든 것: `MenuItemModel` · `DocumentViewModelBase` · `IToolModule`/`IMainShell` · `ToolModules` · 솔루션 모델(`Solution`·`SolutionWorkspace`) · 경로 도우미(`InstallPaths`·`UserDataPaths`·`TrainingPaths`·`ProjectPaths`).
  네임스페이스는 `Minguk.Tools.*` 그대로다(어셈블리 이름만 다르다).
- **모듈 추가**: ① `Minguk.Base`·`Minguk.Tools.Core` 만 참조(셸 참조 금지 - 순환, 아이콘은 `Libs\Minguk.Image.dll` 을 HintPath 로) ② `IToolModule` 구현(`RegisterServices`·`CreateMenuItems`)
  ③ 셸 `App.xaml.cs` 의 `ToolModules.Use(...)` + 셸 csproj `ProjectReference` ④ 하네스 `Program.cs` 의 `ToolModules.Use(...)`(안 넣으면 `--views` 가 안 본다).
- 메뉴 번호는 셸 1000번대, 모듈 2000번대. **리플렉션으로 DLL 을 훑지 않는다.** `MainViewLocator.Assemblies` 가 모듈 어셈블리도 봐야 메뉴가 열린다.
- 모듈 설정은 모듈 화면이 든다(설정 화면에 끼우지 않는다 - 바꾼 결과를 그 자리에서 보게).

## 화면: 화면캡처 · 스크립트 · 플레이

| 화면 | ViewModel | 하는 일 |
|---|---|---|
| 화면캡처 | `CaptureMonitorViewModel : CaptureViewModelBase` | 대상·fps·미리보기·프레임 저장·담기(F8)·**녹화(mp4)**·통계 |
| 스크립트 | `ScriptStudioViewModel : RecognizingCaptureViewModelBase` | VS 모양 편집기. 검출·추적·글자 읽기를 보며 쓰고 돌린다, 빌드, 담기(F8) |
| 플레이 | `PlayViewModel : RecognizingCaptureViewModelBase` | 완성품(.mtsx)을 골라 1회(F5)·반복(F6). 편집 없음. 모델·영역·설정은 완성품의 프로젝트 폴더(`RecognitionRoot`). `설정` 은 값 창(`PlaySettingsView`) |

- 바탕 둘: `CaptureViewModelBase`(잡기·미리보기·입력 전달·저장·담기·통계) 위에 `RecognizingCaptureViewModelBase`(`.Detect.cs`·`.Ocr.cs`·`.Regions.cs`). 캡처는 첫 바탕만 - 모델 메모리를 물리지 않는다.
  파생 화면은 `RestoreSettings/SaveSettings/ReleaseResources` 에서 반드시 `base` 를 부른다. 바탕 이름은 `...Base` 로 끝낸다(`ViewModel` 로 끝나면 로거 이름이 겹친다).
- 설정 키는 **파생 화면 이름**으로 저장된다 - 캡처와 플레이가 대상 창을 각자 기억한다.
- 미리보기 판·입력 전달 도구 줄은 `Views/Parts`(`CapturePreviewPanel`·`PreviewForwardBar`). 겹그림은 판의 `Overlay`, 영역 편집기는 `Editor` 자리.
- **본보기 그림**(2026-09-18): 그림으로 된 메뉴·아이콘은 영역 패널의 `[본보기로 저장]` 으로 `Resources\<영역이름>.png` 를 만들고 스크립트가 `그림누르기("사격장.png")` 로 찾아 누른다
  (`Vision/Matching/TemplateMatch` - 정규화 상호상관, 계단 셋 1/8→1/2→원본, 1080p 0.5초). 밝기는 견디고 크기는 못 견딘다. 자세한 것은 `docs/실시간-스크립트.md`.
  - **마스크**(2026-09-23, 사용자 "폴리곤 식의 구역을 자유롭게 지정해서 이부분만 매칭"): 구역의 다각형(`RegionCell.Mask`, 구역 기준 0~1)을 영역 패널 `[마스크]` 로 놓고 꼭짓점 손잡이(`RegionMaskEditor`, 칸 어도너 위 층)로 고친다.
    저장하면 밖이 투명한 PNG - **알파가 곧 마스크**라 그림찾기 API 는 그대로다. 알파는 본보기만 본다(`GrayImage.From(..., useAlpha: true)`, 화면 조각은 안 본다).
- **미니맵 방향**(2026-09-23, 사용자 "미니맵으로 방향 보면서 처리가 가능해?"): 자동 이동이 안 되는 곳에서 미니맵 화살표로 몸 방위를, 노란 마커로 목표 방위를 읽어 걷는다
  (`방위()`·`목표방위()`·`가기(방위, 밀리초)`, `Vision/Minimap`). 준비는 「미니맵」 이름의 영역 + 도구 줄 `[미니맵 익히기]` 한 번.
  **화살표는 몸 방향이지 카메라 방향이 아니다**(마우스만 돌리면 안 돈다) - 그래서 회전 배율도 걷기를 끼워 잰다. 자세한 것·실측은 `docs/실시간-스크립트.md`.
- **로컬 LLM**(2026-09-24, 사용자 "진짜로 로컬LLM 붙여서" · "스크립트 너 처럼 만들어 줄 수 있는거"): Ollama 어댑터(`Llm/`, 프로젝트 `llm.json` - 글 `qwen3:8b` · 그림 `qwen2.5vl:3b`).
  스크립트 `상황요약`·`판단`(행동 목록 enum 으로 묶는다)·`화면판단`·`판단고치기`, 스크립트 화면 아래 탭 **「AI 도우미」** - 요청 + 지금 화면 → 동작 하나 → 영역을 띄워 사람이 맞추고 [넣기] 하면 본보기 저장 + 코드를 커서 줄 다음에(`Document.Insert`, 되돌리기 된다).
  **이 PC(3GB)에서는 글 판단 3~4초·6~7/8, 3B 그림 모델은 자리·상황을 자주 틀린다** - 분명한 규칙은 스크립트가 먼저, 글자는 OCR 로 읽어 넣는다. 실측·함정은 `docs/실시간-스크립트.md`.
- **자리 안 칸**(2026-09-16): 자리(`NamedRegion`)는 그룹, 칸(`RegionCell`, 자리 기준 0~1, 돌릴 수 있음)만 읽는다. 스크립트 `읽기("자리.칸")`. 칸은 별개 항목·별개 어도너(`RegionCellItem`·`RegionCellAdorner`),
  영역 패널은 트리. 자세한 것·함정(돌린 칸 손잡이 변위는 돌린 좌표계, 회전은 픽셀 공간)은 `docs/검출.md`.
  - **칸은 자리를 "뚫어야" 클릭을 받는다**(2026-09-18, 사용자 "클릭하니까 처음부터 구역이 선택되네"): 새 자리는 자리와 같은 크기의 「전체」 칸으로 시작하고 칸이 자리 위 형제(z 순서 위)라,
    그냥 두면 자리 어디를 눌러도 칸부터 잡혀 자리를 고르거나 옮길 수 없었다. 안 고른 자리의 칸은 `IsHitTestVisible=false` 로 빠져 클릭이 밑 자리로 흘러간다 - **첫 클릭은 자리, 이미 고른 자리를
    또 누르면 뚫려 그 뒤부터 칸**(`RegionCanvas._drilled`·`PlaceCells`). 검사는 실제 커서 없이 `InputHitTest` 대신 아는 요소에 `RaiseEvent` 로 클릭을 태운다(`--vision`, `RegionCanvasSelectionTests`).
- 스크립트 문서는 `ScriptWorkbench`, 실행은 `ScriptPlayer`. 스크립트 입력은 미리보기와 **같은 어댑터**로 나가고 보내기 직전 대상 창을 앞으로.
- **캡처 세션은 허브에서 나눠 쓴다**(`SharedCaptureHub`) - 같은 창을 잡는 화면이 여럿이어도 WGC 세션은 하나. 리드백은 누구라도 원하면 켜고, 세션을 만들 때 정해져 필요하면 새로 만든다.
  콜백은 잠금 없이 손잡이 배열 스냅샷을 돈다. **세션이 스스로 멈추면**(대상 창 닫힘, `IScreenCaptureAdapter.Ended`) 허브가 손잡이를 모두 멈춘 것으로 하고 알리고, 화면 바탕이 중지 상태로 맞춘 뒤
  `OnCaptureEnded` 로 파생 화면(스크립트·플레이)이 도는 스크립트를 세운다(사용자, 2026-09-18) - 없던 때는 화면이 캡처 중인 줄 알아 스크립트가 옛 검출로 계속 돌았다.
- **fps 상한은 `FrameRateLimiter`**(캡처 세션 WGC·영상, 미리보기) - 1/fps 박자에 맞추고 박자보다 1/3 간격 넘게 이른 장만 버린다.
  예전 "직전 도착 + 90%" 규칙은 도착 간격이 흔들리는 모니터(이 PC 5대, 59/60Hz)에서 상한 60 에 **31~58fps** 였다 → 고친 뒤 58.8~60.0(실측 `--capture-fps`).
  WGC 는 화면이 바뀔 때만 준다 - 30fps 방송 창을 잡으면 30 이 맞다. 경로 탓인지 내용 탓인지는 `--capture-fps`(움직이는 창을 띄워 잰다)로 가른다.
- **스크립트 화면**(`docs/스크립트-프로젝트-설계.md`): BarManager(메뉴·도구 모음 `Bars` 에 `x:Name`·상태 줄) + 화면 안 DockLayoutManager(미리보기·솔루션 탐색기·오류 목록·출력·호출·변수·영역·중단점).
  - 탭 편집기의 데이터 문맥은 **문서** - 공용 설정은 문서가 든 `Settings`(워크벤치)로 묶는다(떠 있는 창에서 조상 찾기가 끊긴다).
  - 탭 활성화는 한 방향으로만 기다린다(`ScriptDocumentsBehavior._requested`) - 늦게 오는 `DockItemActivated` 와 핑퐁이 났다.
  - 기본 배치를 바꾸면 `DockLayoutVersion` 을 올린다. 도구 모음 자리는 `BarLayout` 설정. 미리보기 | 문서 나누기는 `DesignSplitGroup`(`SetSplit`·`SwapPanes`).
- **녹화**(`CaptureMonitorViewModel.Recording.cs`, 어댑터 `IVideoRecorder`·`MediaFoundationVideoRecorder`·`VideoRecorderFactory`): 프로젝트 `Recordings\*.mp4`(`ProjectPaths.Recordings`), H.264.
  **fps 는 화면캡처 fps 콤보 값**이고 더 빨리 오는 프레임은 솎는다(세션을 나눈 다른 화면이 더 높은 fps 를 원하면 세션이 그 fps 로 돈다). **비트레이트는 화면 크기·fps 에 맞춘다**(`DefaultBitrate` = 가로×세로×fps×0.9비트, 1080p30 ≈ 56Mbps·1분 420MB).
  8Mbps 고정이던 것을 올렸다(사용자, 2026-09-16 "용량은 상관없어") - 얇은 흰 획이 뭉개져 영상을 캡처 대상으로 놓으면 글자 읽기가 20장 중 3장이었고(화면 사진은 20/20),
  라벨링에서 뽑아 학습하는 그림도 같이 나빠진다.
  캡처 스레드는 픽셀만 복사해 넘기고 쓰기 스레드가 SinkWriter 를 첫 프레임 크기로 만들어 쓴다(0.2초어치 넘게 밀리면 버린다 - 1080p 30·60fps 실측 버림 0).
  리드백을 알아서 켜는데 그때 세션이 다시 시작되므로 **녹화기는 그 뒤에** 만든다. 입력은 RGB32 + 양수 `DefaultStride`(안 적으면 뒤집힌다), 크기는 짝수로 자른다.
  캡처가 멈추거나 크기가 바뀌면 파일을 닫는다. 길이 제한은 없고 디스크가 한도다.
  **한 녹화 = 한 파일, 조각 mp4**(`MFCreateFile` + `MFCreateFMPEG4MediaSink` + `MFCreateSinkWriterFromMediaSink`) - 쓰는 도중에도 조각(moof)이 디스크에 붙어 **앱이 죽어도 그때까지는 튼다**
  (사용자 요구 "10초·1분마다 확정"). 싱크를 직접 만들면 SinkWriter 가 싱크를 안 내린다 - 싱크 `Shutdown`·바이트 스트림 `Close` 는 우리가.
  **조각 간격은 싱크가 정하고 키프레임과 무관**(실측 2026-09-15): 약 7~9장마다(1080p 30fps 0.25초 · 60fps 0.13초), 키프레임은 [0,9,90]. `MaxKeyframeSpacing` 은 1초·10초가 바이트까지 같아 안 먹는다.
  그래서 간격 콤보(`나눠 쓰기`/`RecordingSegment`)는 **없앴다** - 요구보다 촘촘히 확정되고, 효과 없는 콤보는 되는 줄 알게 만든다.
  검사 `--vision` 의 녹화 줄: 쓰는 도중 파일을 떠 둔 사본을 MF 로 풀어 프레임을 읽는다(40장→36 · 75장→72), 닫으면 75/75.
- **영상을 캡처 대상으로**(사용자, 2026-09-15 - 게임 없이 검출·글자 읽기·스크립트 흐름 시험): 대상 콤보 맨 아래 `[영상] 파일.mp4`(프로젝트 `Recordings`, 새것부터).
  `CaptureTargetKind.Video` + `CaptureTarget.FilePath`, 구현 `VideoFileCaptureSession`(SourceReader 고급 영상 처리 → RGB32 → 위에서 아래·알파 255 로 고쳐 D3D 텍스처 + 리드백 픽셀).
  녹화 속도대로 흐르고 끝나면 되감는다, fps 솎기는 WGC 와 같은 90% 규칙. 핸들이 모두 0 이라 허브·선택 복원은 `CaptureTarget.Key`(영상은 경로)로 가른다.
  **자리는 영상 픽셀**(`CaptureTargetBounds` 가 (0,0,w,h), 크기는 `TryGetFrameSize` 캐시) - 검출 좌표가 그대로 나온다.
  **입력은 절대 안 나간다**: 미리보기는 `PreviewInputRouter` 가 `InputForwardResult.VideoTarget`(안 막으면 영상 좌표로 진짜 화면 왼쪽 위를 누른다),
  실시간 스크립트는 `LiveScriptSession.BuildHost` 가 `InputAdapterFactory.CreateSilent()`(부른 것은 호출 로그에만)로 바꾸고 조준 배율 학습을 끈다(화면이 안 돌아 배율이 끝없이 커져 저장된다).
  검사 `--vision` 의 `영상 대상:` 줄(1080p 크기·방향·속도·되감기·솎기·허브·입력 막기).
- **학습하는 동안 검출은 GPU 모델을 내려놓는다**(`TrainingActivity`) - 3GB 카드에서 캡처·DirectML 추론·CUDA 학습이 겹쳐 GPU 가 리셋됐다(실측). 리셋 뒤에는 앱을 다시 켜야 해 그렇게 알린다(`IsGpuLost`).
- **버튼을 숨긴 설정은 저장값을 믿지 않는다** - 화면캡처 `미리보기` 는 `RestoreSettings` 에서 늘 켠다(숨긴 채 False 로 저장되면 켤 길이 없다). 그리드 배치를 복원한 뒤 자동 너비를 다시 건다.
- **서비스를 View 에 선언하지 않으면 `Guard` 가 예외를 삼켜 아무 일도 안 일어난 것처럼 보인다** - 새 서비스를 쓰면 View 의 `Interaction.Behaviors` 부터 본다.

## ViewModel 작성 규칙

`DocumentViewModelBase` 가 생명주기·서비스·예외를 든다. 화면은 필요한 단계만 채운다(`DashboardViewModel` 이 최소 예시).

| 파일 | 담는 것 |
|---|---|
| `XxxViewModel.cs` | 생명주기 — `Create()` / 생성자 / `Initialize*` / `Save*` / `Release*` |
| `XxxViewModel.Model.cs` | 상태 — 바인딩 프로퍼티, 커맨드 선언, 컨트롤 참조 |
| `XxxViewModel.Code.cs` | 동작 — 커맨드가 하는 일 (길어질 때만) |

View 의 `Loaded` 와 부모 주입이 **모두** 오면 한 번 돈다:
`InitializeControls()`(컨트롤 참조) → `InitializeObservable()`(구독, `Disposables` 에) → `RestoreSettings()` → `OnLoaded()`.
닫힐 때 `OnClose` → `OnDestroy` → `SaveSettings` → `ReleaseResources` → 구독·메신저 해제. 초기화 안 된 화면(안 연 탭)의 `SaveSettings` 는 바탕이 건너뛴다.

- 생성자에는 XAML 이 바인딩할 것만(커맨드·컬렉션·Caption). `OnInitializedCommand`·`OnClosingCommand`·`DoCloseCommand` 는 바탕 것.
- 로거는 바탕의 `Logger`(새로 만들지 않는다). 메신저는 `OnMessenger` 를 override - **화면이 뜬 뒤에야** 받는다(방금 연 화면에 보낼 요청은 들고 있다가 `OnLoaded` 에서 가져간다).
- 예외는 `Guard(() => ...)` - NLog + `ExceptionViewer`. 복구가 필요한 곳만 `try/catch`.
- **이벤트는 `+=` 로 걸지 않는다 - `RxEvents`(Core `Helper`) + `.Listen(...)` 으로 구독해 `IDisposable` 로 든다**(사용자, 2026-09-19).
  - `Subscribe` 말고 `Listen` - Rx 는 받는 쪽이 한 번 던지면 구독을 끊는다(`+=` 는 다음 알림도 받았다). `Listen` 은 로그만 남기고 살려 둔다.
  - 둘 곳: 정적 이벤트·컨트롤·서비스는 `InitializeObservable`/`InitializeControls` 에서 `Disposables` 에(바탕이 **`ReleaseResources` 앞**에서 끊는다).
    화면이 만든 것(`Player`·`Script`·`Live`)은 생성자에서 - 하네스가 화면 없이 ViewModel 을 쓴다. 닫으며 부르는 `Player.Stop()` 이 실행 끝남을 거쳐야 해서 스크립트·플레이 화면은 `_ownEvents` 로 `ReleaseResources` **끝**에서 끊는다.
    도중에 바뀌는 대상(캡처 세션·파일 감시·행마다 `PropertyChanged`)은 `SerialDisposable`·구독표로 들고 예전 `-=` 자리에서 끊는다.
  - 다른 스레드에서 오는 알림을 화면에 반영할 때 `.ObserveOnUi()`, 몰려오는 알림은 `.Throttle(...)`(탐색기 폴더 감시).
  - View 코드 비하인드가 제 자식 컨트롤에 거는 것(`Loaded`·버튼 `Click`)은 같이 사라지므로 `+=` 그대로 둔다.
- **정적 이벤트**(`SolutionWorkspace.Changed`·`LightweightThemeManager.CurrentThemeChanged` 등)는 닫힐 때 반드시 푼다(위 규칙대로 `Disposables` 에 넣으면 된다).

## 외부와 닿는 것은 어댑터로

OS·하드웨어·외부 라이브러리는 **인터페이스 + 구현 + 팩터리**. 구현이 하나여도 인터페이스를 만들고, 새 구현은 팩터리만 고치고, 인터페이스에 `Name` 을 둔다.

| 인터페이스 | 구현 | 고르는 곳 |
|---|---|---|
| `IScreenCaptureAdapter` | `WgcCaptureSession` · `VideoFileCaptureSession`(영상) | `ScreenCaptureAdapterFactory` · `SharedCaptureHub` |
| `IInputAdapter` | `SendInputAdapter` · `WindowMessageInputAdapter` · `InterceptionInputAdapter` | `InputAdapterFactory` |
| `IUiAutomationAdapter` | `WindowsUiAutomationAdapter` | `UiAutomationAdapterFactory` |
| `IGlobalHotkeyAdapter` | `GlobalHotkeyAdapter` | `GlobalHotkeyAdapterFactory` · `SharedHotkeysFactory` |
| `IWindowTargetAdapter` | `Win32WindowTargetAdapter` | `WindowTargetAdapterFactory` |
| `IOcrEngine` | `PaddleOcrEngine`(PP-OCRv5 GPU·CPU / PP-OCRv6 tiny·small - `PaddleOcrModelSet`, v6 는 한글 없음·det 정규화 다름) · `WindowsOcrEngine`(Windows 내장) | `OcrEngineFactory` - 스크립트 화면 인식 도구 줄의 「글자 읽기」 콤보(`OcrEngineChoice`, 앱 전체 설정 키 `Minguk.Tools.Ocr.Engine`) |

- **능력이 경로마다 다르면 능력별 인터페이스**(`IScanCodeInput` SendInput·Interception / `ICharacterInput`·`IImeControl` PostMessage·SendMessage). 부르는 쪽이 `adapter is IXxx` 로 묻는다 -
  늘 false 인 빈 메서드는 되는 줄 알고 쓰게 만든다.
- `CreateWithFallback` 은 다른 경로로 내려앉되 `FellBackFrom`·`Reason` 을 돌려준다 - 조용히 다른 길로 보내지 않는다.
- **창 메시지 경로**(`WindowMessageInputAdapter` = PostMessage + `SendMessageTimeout`, 맨 `SendMessage` 금지):
  - 글자는 반드시 `WM_CHAR` - 키로 보내면 부친 Shift 가 안 먹어 `abC!` → `abc1`.
  - P/Invoke 는 **W 판을 명시**(`PostMessageW`) - ANSI 판이 잡히면 한글이 `?`.
  - 한/영 은 `WM_IME_CONTROL`(변환 모드). `WM_INPUTLANGCHANGE`(자판)와 다르다.
  - **게임에는 안 먹는다**(Raw Input·DirectInput) - 게임은 SendInput 이나 Interception. 보낼 창은 `IWindowTargetAdapter` 로 고른다.
- 입력 어댑터는 `Minguk.Tools.Input/`, `Minguk.Tools.Capture/Input/` 에는 미리보기 좌표 계산만(`PreviewInputRouter` 등). 게임 입력 함정은 `docs/실시간-스크립트.md`.

## 스크립트

- 엔진: C#(`RoslynScriptEngine`, 기본) · JavaScript(`JavaScriptEngine`, Jint) · Python(`PythonScriptEngine`, 첫 선택 때 CPython 11MB). 실시간 모드·조준·디버그는 `docs/실시간-스크립트.md`.
- **입력 테스트의 계획 모드**는 세 겹: 글(`SequenceScript`) → 데이터(`SequencePlan`) → 델리게이트(`InputSequence`). 시작할 때 한 번 굳힌다.
  `SequenceStepKind` 는 **늘리기만**(JSON 에 이름으로). 틀린 줄이 있어도 파싱은 끝까지, 실행은 막는다.
- Roslyn 컴파일 어셈블리는 언로드되지 않는다 - 타이핑 500ms 묶기 + 같은 글 캐시 + 10분 붙들기(`KeepAlive`).
- 파이썬은 한 프로세스에 한 번만 켠다(Shutdown 뒤 재초기화가 깨진다). 파이썬 객체는 `Py.GIL()` 안에서만. 열거형은 `typeof(T).ToPython()` 이 안 된다.
- **프로젝트** `.mtsproj`(`ScriptProject`) - 목록에 있는 것만 프로젝트다. 파일은 폴더 밖을 못 가리키고, 공유는 `ProjectReference`(`../공용/공용.mtsproj`)로 한다.
  `ToUnit` 이 참조한 프로젝트의 소스(시작 파일 빼고)·DLL 참조를 앞에 합친다 - 실행·검사·완성·빌드가 다 `ScriptUnit` 을 본다.

### 빌드 - IL(.mtsx)

- **진입점을 우리가 쥔다**(`CompiledScriptBuilder`): 프로젝트 글을 `public class __Compiled : LiveScriptApi` 의 `__Run()` 몸으로 감싸 **일반 C# 컴파일**. 스크립팅 emit 은 Roslyn 내부 factory 에 기대
  Roslyn 을 올리면 옛 파일이 안 돈다. `EntryTypeName`·`EntryMethodName` 은 바꾸지 않는다. `LiveScriptApi` 는 그래서 `sealed` 가 아니다.
- 조립: `using` 은 위로, 최상위 문장·지역 함수는 `__Run` 몸으로, 타입은 중첩 멤버로. 손으로 적은 `#load` 는 안 쓴다(소스는 `ScriptUnit.Sources` 로 온다), `#r` 은 긁어 참조에 더한다. `CS1998` 은 끈다.
- 빌드(Ctrl+Shift+B) → `<프로젝트>\bin\<이름>.mtsx`. `Resources\` 는 옆에 복사. **바깥 DLL(#r·참조 항목)도 bin 에 복사**(`CopyReferences`, 앱·런타임 폴더 것은 뺀다).
- 실행(`CompiledScriptRunner`): `Assembly.Load(bytes)` → `__Run()`. 도는 동안 `AssemblyResolve` 로 `.mtsx` 옆에서 DLL 을 찾고(형식은 JIT 때 올라온다), 못 찾으면 이름과 둘 자리를 말한다.
  host·비상 정지·끝맺음은 소스 실행과 나눠 쓴다(`LiveScriptSession.ResolveCompiled`). `.mtsx` 는 편집 못 한다 - Minguk.Tools 안에서만 돈다.

### 스크립트 파일 · 편집기

- 확장자로 언어를 가른다(`.csx`·`.py`·`.js`, `ScriptFiles`). 열 때 **언어를 먼저 바꾸고** 글을 넣는다. 모르는 확장자면 언어를 그대로 둔다.
- 언어별로 따로 기억한다(`Script.CSharp` 등). 언어를 바꿔도 쓰던 글은 안 버리고, **손대지 않은 본보기**만 새 언어 본보기로 갈아 끼운다.
- 글은 설정에도 파일로도 남는다. `*` 는 열어 둔 파일과 다르다는 표시. 밖에서 고치면 다시 읽되(300ms 묶기, 잠김 재시도) 여기서 고친 것이 있으면 덮지 않고 알린다.
- 파일은 **UTF-8(BOM 없이)**. 대화 상자는 `dxmvvm:OpenFileDialogService`/`SaveFileDialogService`.
- 편집기는 **AvalonEdit**(AvaloniaEdit 아님)을 **`Markup/ScriptEditor` 로 감싸서** 쓴다 - 바로 얹으면 창 전체의 UI 자동화 트리가 빈다.
  - `TextEditor.Text` 는 의존 속성이 아니다(`Markup/AvalonEditText`). 경량 테마가 안 칠하므로 색은 `Helper/SequenceScriptHighlighting` 이 든다.
  - 밝기는 **경량 테마 팔레트의 `Brush.Editor.Background` 밝기**로 가른다(테마 이름·키로 가르지 않는다). 칠하는 색은 VS 편집기 값(어두움 #1E1E1E/#DCDCDC).
  - 낱말 색은 이 PC 의 VS 2026 + ReSharper 화면에서 잰 값이다(색표는 `Resource/SequenceScript.*.xshd` 한 곳, 분류 이름 = Color name).
    C# 은 Roslyn 분류로 덧칠한다(`IScriptClassifier`·`SemanticColorizer`, 250ms 멎으면). 최상위 변수는 제출 클래스의 필드라 멤버 색이 맞다.
  - 완성: C# 은 Roslyn(`RoslynCompletionSource` - 제출 프로젝트 + `hostObjectType`, `DocumentInfo` 도 Script, 첫 호출 1.4초라 `WarmUpAsync`), 파이썬·JS 는 `ScriptApiCatalog`. 이름만 넣는다.
  - 참조 표시(CodeLens, `IScriptReferenceFinder`·`CodeLensGenerator`) - `#load` 로 이은 컴파일의 모든 구문 트리에서 센다.
  - 빨간 밑줄은 배경 렌더러. 오프스크린에서는 TextView 층이 안 그려진다 - 검사는 렌더러 `Draw` 를 직접 부르고 화면 밖 진짜 창에 담는다.
- **읽기 전용 속성을 `EditValue` 에 묶을 때는 `Mode=OneWay`** - 기본 TwoWay 라 화면 생성 때 터진다(`--views` 가 잡는다).

## 규칙

- **화면 규칙(사용자)**
  - **화면에 보이는 글은 모두 한국어**(사용자, 2026-09-15) - 버튼·툴팁·상태 줄·아래 바·대화 상자·오류 안내. 영어 예외 문장(.NET·COM·Media Foundation·
    파이썬)을 그대로 띄우지 않는다: 무엇을 하다 실패했는지·어떻게 하면 되는지를 한국어로 적고, 원문은 로그에 남긴다(필요하면 코드만 괄호로, 예: `(0x887A0005)`).
    이름이 굳은 것(fps·GPU·ONNX·YOLO·mp4·H.264·F5 같은 키)은 그대로 둔다. 로그(NLog)는 한국어가 기본이되 원문 예외는 그대로.
  - **모든 것을 Visual Studio 2026 과 DevExpress WPF 기준으로**(VS Code 아님) - 배치·동작·메뉴·도구 모음·단축키.
  - 배치는 **LayoutControl**(`dxlc:`). Grid·StackPanel 로 뼈대를 짜지 않는다. VS 같은 도킹 화면의 큰 틀만 DockLayoutManager.
  - 메뉴·도구 모음·상태 표시줄은 BarManager. **목록·표·트리는 GridControl**(트리는 `TreeListView`). 컨트롤은 대부분 DevExpress(없는 것만 표준 WPF).
  - **한 줄에 놓인 컨트롤은 모두 세로 가운데 정렬** - 글자 템플릿의 `TextBlock` 에 `VerticalAlignment="Center"`. 도구 줄 글자 템플릿은 `Minguk.Base/Resource/Style.xaml`
    (`BarStaticItem`·`BarButtonItem`·`BarCheckItem`·`BarEditItem`). 글자 상자가 칸 높이로 늘어나면 글자는 상자 위에 붙어 뜬다.
    검사 `VerticalAlignmentCheck`(`--labeling-screen`·`--script-screen`·`--environment-screen`·`--play-screen`) - 화면을 찍는 검사를 새로 만들면 넣는다.
  - 칸 옆 버튼은 `dxe:ButtonEdit` 안에 넣고 `AllowDefaultButton="False"` 를 같이 적는다(안 적으면 빈 `…` 버튼이 하나 더 붙는다). 그림 버튼은 `GlyphKind="User"` + `Image`(가운데 정렬).
  - `dxlc` 에는 `ItemHeight`·`ShowLabel` 이 없다 - 라벨을 비우려면 `AddColonToLabel="False" Label=""`.
- 폰트·크기는 `BaseFontFamily`/`BaseFontSize` 를 **DynamicResource** 로. 경량 테마(`UseLightweightThemes`)라 표준 WPF 컨트롤은 테마를 안 탄다.
- 그리드는 `BaseGridControl`/`BaseTableView`. **칸 너비는 내용에 맞춘다**(사용자, 2026-09-16) - `dependency:GridControlDependency.IsColumnAutoWidth="True"`(`AutoWidth` 는 안 쓴다),
  열에는 `Width` 를 안 적는다(적으면 그 값에 묶여 글자가 잘린다). 사용자가 끈 너비를 남겨야 하는 그리드(화면캡처 통계)만 예외로 끈다.
  - 행 번호(`IsRowNumber`)를 쓰면 `IndicatorWidth` 를 숨은 `Border`(`SharedSizeGroup="RowNumberGroup"`)의 폭으로 되돌린다(`CaptureMonitorView.xaml` 예시) - 안 하면 세 자리부터 잘린다.
  - `IsColumnAutoWidth` 를 켜면 열 너비가 Auto 라 사용자가 끈 너비가 안 남는다.
  - 트리(`BaseTreeListView`)도 열 머리글이 **기본으로 보인다**(사용자, 2026-09-16 - 늘 꺼져 영역 그리드에 머리글이 없었다). 열이 하나뿐인 트리는 그 화면 XAML 에서 `ShowColumnHeaders="False"`(솔루션 탐색기).
  - 저장된 배치의 `ActualShowSearchPanel` 은 복원 전에 지운다. 하네스도 `DataControlBase.AllowInfiniteGridSize = true`.
- `ItemsSource` 가 있는 콤보는 **`SelectedItem`**(`Mode=TwoWay, UpdateSourceTrigger=PropertyChanged`)으로 묶는다 - `EditValue` 면 화면과 실제 동작이 어긋난다.
- DevExpress 내장 SVG(`dx:DXImage`)는 없는 경로면 런타임에 터진다 - 아이콘은 확인된 Axialis(`image:FreeImage`)를 쓴다.
- ViewModel 사이는 `MessengerUtility`. 예외 표시는 `ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName())`.
- **DirectML 네이티브**는 `Libs/DirectML/x64/` 에서 나간다(패키지는 셸·Ocr 프로젝트 둘 다 `ExcludeAssets="all"`, onnxruntime 판도 둘이 같아야 한다). 올릴 때 새 패키지의 DLL 로 덮고 같이 커밋 - onnxruntime 과 짝이 안 맞으면 세션 만들 때 터진다.
  `DirectML.Debug.dll` 은 Debug 구성만(csproj 에서 나눈다). `.pdb` 는 저장소에 안 둔다.
- **Interception**: `interception.dll`·`install-interception.exe`(같은 v1.0.1)는 `Minguk.Tools.Input/Native/x64/`(x86 이면 `BadImageFormatException`).
  상태는 `keyboard`·`mouse` 서비스로 읽고, 설치는 별도 프로세스 `runas`(앱 전체를 관리자로 올리지 않는다), 설치 뒤 **재부팅**.
- `Minguk.Image` 는 솔루션에 없다 - `Libs/Minguk.Image.dll` 을 HintPath 로 문다(리소스 4만 개). 아이콘을 바꾸면 그 프로젝트를 Release|x64 로 따로 빌드해 덮고 커밋.

## 검증

`Minguk.Tools.Tests` 는 단위 테스트가 아니라 **실행형 하네스**다(`Minguk.Tools.Tests/README.md`). 앱이 켜져 있으면 출력 복사가 잠겨 빌드가 실패한다(컴파일 오류와 구별).

```
dotnet run --project Minguk.Tools.Tests -c Debug -- --views               # 화면·모듈 생성 (안전)
dotnet run --project Minguk.Tools.Tests -c Debug -- --vision              # 라벨·검출·OCR·스크립트·빌드·조준(가짜 어댑터) (안전)
dotnet run --project Minguk.Tools.Tests -c Debug -- --aim                 # 조준 스레드만 - 가짜 게임으로 꺾기·지나침·달리는 검출·배율 배우기 + 메뉴 글자 찾기 (안전, 40초)
dotnet run --project Minguk.Tools.Tests -c Debug -- --check-project=<.mtsproj> # 작업공간의 진짜 프로젝트가 컴파일되는지만 (안전, 안 돌린다)
dotnet run --project Minguk.Tools.Tests -c Debug -- --ocr                 # 글자 읽기만 (안전, CPU·GPU)
dotnet run --project Minguk.Tools.Tests -c Debug -- --minimap             # 미니맵 방향 읽기만 - 실제 게임 화면 5장 (안전, 1초)
dotnet run --project Minguk.Tools.Tests -c Debug -- --ocr-bench --project=<프로젝트> --truth=Minguk.Tools.Tests/OcrData/<게임>.tsv   # 실제 화면 정답률
dotnet run --project Minguk.Tools.Tests -c Debug -- --solution            # 솔루션·공유 프로젝트·탐색기·▶ 찾기 (안전, 임시 폴더)
dotnet run --project Minguk.Tools.Tests -c Debug -- --script-screen       # 스크립트 화면 화면 밖 띄우기·바인딩 오류·정렬·PNG (안전)
dotnet run --project Minguk.Tools.Tests -c Debug -- --labeling-screen     # 라벨링 화면 패널 높이·정렬·PNG (안전)
dotnet run --project Minguk.Tools.Tests -c Debug -- --environment-screen  # 환경설정 화면 두 칸·아이콘·정렬·PNG (안전)
dotnet run --project Minguk.Tools.Tests -c Debug -- --play-screen [--width=1150] # 플레이 화면 도구 줄 두 줄·넘쳐 접힘·상태 표시줄·정렬·PNG (안전)
dotnet run --project Minguk.Tools.Tests -c Debug -- --live-ocr --project=<프로젝트>  # 켜 둔 게임 창을 잡아 자리·칸을 읽고 조각 PNG (게임이 떠 있어야)
dotnet run --project Minguk.Tools.Tests -c Debug -- --settings-screen     # 설정 탭 미리보기·디자인·값 쓰기·정렬·PNG (안전, 임시 폴더)
dotnet run --project Minguk.Tools.Tests -c Debug -- --detect-check        # 모델이 라벨을 다시 찾는지(재현율)
dotnet run --project Minguk.Tools.Tests -c Debug -- --backend=SendInput   # 입력 경로 전체 (커서·키보드를 가져간다)
dotnet run --project Minguk.Tools.Tests -c Debug -- --canvas-drag         # 라벨 캔버스 실제 마우스 끌기 (커서)
dotnet run --project Minguk.Tools.Tests -c Debug -- --region-drag [--busy] # 영역·구역 손잡이 실제 마우스 끌기 - 끄는 동안 커서와 벌어짐 (커서)
dotnet run --project Minguk.Tools.Tests -c Debug -- --list-typing           # 설정 목록 칸 표 새 행 줄에 실제 키보드로 적기(Tab·Enter, 대화 상자 창) (커서·키보드)
```

- 화면(XAML)을 고쳤으면 `--views` - XAML 은 빌드를 통과하고 런타임에 터진다. 그 화면을 찍는 검사가 있으면 그것도.
- 라벨·검출·스크립트만 고쳤으면 `--vision`. 솔루션·프로젝트·작업 공간이면 `--solution`.
- 입력 쪽을 고쳤으면 **세 경로 모두**(`--backend=`). 한글은 메모장으로도 쳐 본다(하네스 대상이 WPF 라 IME 문제를 못 잡는다).
- 창(MainWindow)은 하네스가 안 만든다 - 제목 표시줄을 고쳤으면 앱을 잠깐 띄워 본다.
