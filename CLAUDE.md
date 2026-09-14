DevExpress WPF 컨트롤, WPF 개발자

## 이 프로젝트

TamsTools 의 셸 구조(MainWindow / MainView / MainViewModel / MainMenu)와 기준 스타일(Minguk.Base)을 가져온 게임 자동화 도구.
화면캡처 → 라벨링 → 학습(ONNX) → 스크립트(몹 찾기·조준·글자 읽기) → 빌드(.mtsx) → 플레이.

자세한 규칙·실측은 docs 에 나눠 둔다 - 그 영역을 고칠 때 먼저 읽는다.

| 문서 | 무엇 |
|---|---|
| `docs/프로젝트-설계.md` | 솔루션·프로젝트·작업공간, 폴더 모양, 솔루션 탭·셸 |
| `docs/몹-검출.md` | 라벨 형식, ONNX 모델·DirectML 함정, 학습, 실시간 몹 찾기, OCR·이름 붙인 자리, 라벨링 화면 |
| `docs/실시간-스크립트.md` | 실시간 API·안전장치, 목표·조준, 입력 경로, 전역 단축키, 디버그 |
| `docs/스크립트-프로젝트-설계.md` · `docs/스크립트-설계.md` | 스크립트 화면(VS 모양)·프로젝트 파일 |
| `docs/ONNX-모델-학습.md` · `docs/몹-찾기-속도-설계.md` | 학습 절차 · 추론 속도 |

## 짜임

```
Minguk Tools (틀)
├ 자동화                      ← 그룹
│  ├ 환경설정                 ← 모듈(Minguk.Tools.Training). 작업공간·학습 경로를 정한다
│  ├ 솔루션                   ← AutomationMainView. 솔루션이 없으면 시작 창
│  │    위 : 작업공간 → [솔루션 ▾] → [프로젝트 ▾]  [새 프로젝트][새 공유 프로젝트][새 솔루션]
│  │    아래 탭 : 화면캡처 · 라벨링 · 스크립트   ← 고른 프로젝트를 따라간다
│  └ 플레이                   ← bin 의 완성품(.mtsx)을 돌린다
└ 입력 테스트                 ← 입력 경로 점검(계획 모드)
제목 표시줄 : [▶ 시작 프로젝트] [중지] · 최상위 · 테마      창 제목 : 사격장 - 오버워치 - Minguk Tools
```

- 게임 하나 = **솔루션**(`.mtsln`), 모드·스테이지·런 = **프로젝트**(`.mtsproj`). VS 2026 과 같은 말이다. 사진·라벨·모델·영역·스크립트는 **프로젝트마다 제 것**.
- 솔루션들이 든 폴더를 화면에서 **작업공간** 이라 부른다(설정 키 `Vision.ProjectsRoot`·`ProjectPaths` 이름은 저장값 때문에 그대로). 작업공간은 솔루션 폴더의 **위** 폴더다.
- **화면들이 고른 프로젝트를 본다** - 기준은 `SolutionWorkspace.StartupDirectory` 하나: 데이터셋·모델·영역(`LabelDataset.ConfiguredRoot`), 스크립트 자리(`ScriptFiles.DefaultDirectory`),
  프레임 저장(`ProjectPaths.Captures`), 스크립트 화면이 여는 `.mtsproj`. 솔루션이 없을 때만 옛 자리(`LegacyRoot`)를 본다.
  이름 주의: `Minguk.Tools.Input.Scripting` 안에서 `Projects.` 는 `Input.Scripting.Projects` 로 잡혀 `global::Minguk.Tools.Projects` 로 적는다.
- 자세한 것(솔루션 탭 문서·프로젝트 바꾸기·공유 프로젝트·솔루션 탐색기·▶ 실행)은 `docs/프로젝트-설계.md`.

## 화면 하나 추가하는 법

**새 기능은 모듈 프로젝트로 만든다**(아래). 셸 안에 화면을 더할 때는:

1. `Views/XxxView.xaml` + `.xaml.cs` (UserControl)
2. `ViewModels/XxxViewModel.cs` — `DocumentViewModelBase` 상속, `Create()` 팩터리, 생성자에서 `Caption`/`CaptionImage`(탭 캡션 = 메뉴 이름)
3. `App.xaml.cs` 의 `builder.Services.AddTransient<XxxView>()` - 빠뜨리면 메뉴는 보이는데 탭이 비어서 열린다(`MainViewLocator` 가 DI 에서 꺼낸다)
4. `Source/MainMenu.cs` 에 `MenuItemModel.Create(...)`(`class_nm` = View 전체 타입 이름). 솔루션 탭 아래 화면이면 `Source/AutomationScreens.cs`.

대시보드(`DashboardView`)는 메뉴에서 빼고 ViewModel 최소 예시로 남겨 두었다.

## 기능 모듈 - 셸은 뼈대, 기능은 프로젝트

```
Minguk.Tools.Core        ← 셸과 모듈이 함께 보는 가운데 조각 (기능 코드는 넣지 않는다)
   ↑              ↑
Minguk.Tools    Minguk.Tools.Training     ← 모듈. 화면 + 메뉴를 들고 온다
```

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
| 스크립트 | `ScriptStudioViewModel : RecognizingCaptureViewModelBase` | VS 모양 편집기. 몹 찾기·추적·글자 읽기를 보며 쓰고 돌린다, 빌드, 담기(F8) |
| 플레이 | `PlayViewModel : RecognizingCaptureViewModelBase` | 완성품(.mtsx)을 골라 1회(F5)·반복(F6). 편집 없음. 모델·영역은 완성품의 프로젝트 폴더(`RecognitionRoot`) |

- 바탕 둘: `CaptureViewModelBase`(잡기·미리보기·입력 전달·저장·담기·통계) 위에 `RecognizingCaptureViewModelBase`(`.Detect.cs`·`.Ocr.cs`·`.Regions.cs`). 캡처는 첫 바탕만 - 모델 메모리를 물리지 않는다.
  파생 화면은 `RestoreSettings/SaveSettings/ReleaseResources` 에서 반드시 `base` 를 부른다. 바탕 이름은 `...Base` 로 끝낸다(`ViewModel` 로 끝나면 로거 이름이 겹친다).
- 설정 키는 **파생 화면 이름**으로 저장된다 - 캡처와 플레이가 대상 창을 각자 기억한다.
- 미리보기 판·입력 전달 도구 줄은 `Views/Parts`(`CapturePreviewPanel`·`PreviewForwardBar`). 겹그림은 판의 `Overlay`, 영역 편집기는 `Editor` 자리.
- 스크립트 문서는 `ScriptWorkbench`, 실행은 `ScriptPlayer`. 스크립트 입력은 미리보기와 **같은 어댑터**로 나가고 보내기 직전 대상 창을 앞으로.
- **캡처 세션은 허브에서 나눠 쓴다**(`SharedCaptureHub`) - 같은 창을 잡는 화면이 여럿이어도 WGC 세션은 하나. 리드백은 누구라도 원하면 켜고, 세션을 만들 때 정해져 필요하면 새로 만든다.
  콜백은 잠금 없이 손잡이 배열 스냅샷을 돈다.
- **fps 상한은 `FrameRateLimiter`**(캡처 세션 WGC·영상, 미리보기) - 1/fps 박자에 맞추고 박자보다 1/3 간격 넘게 이른 장만 버린다.
  예전 "직전 도착 + 90%" 규칙은 도착 간격이 흔들리는 모니터(이 PC 5대, 59/60Hz)에서 상한 60 에 **31~58fps** 였다 → 고친 뒤 58.8~60.0(실측 `--capture-fps`).
  WGC 는 화면이 바뀔 때만 준다 - 30fps 방송 창을 잡으면 30 이 맞다. 경로 탓인지 내용 탓인지는 `--capture-fps`(움직이는 창을 띄워 잰다)로 가른다.
- **스크립트 화면**(`docs/스크립트-프로젝트-설계.md`): BarManager(메뉴·도구 모음 `Bars` 에 `x:Name`·상태 줄) + 화면 안 DockLayoutManager(미리보기·솔루션 탐색기·오류 목록·출력·호출·변수·영역·중단점).
  - 탭 편집기의 데이터 문맥은 **문서** - 공용 설정은 문서가 든 `Settings`(워크벤치)로 묶는다(떠 있는 창에서 조상 찾기가 끊긴다).
  - 탭 활성화는 한 방향으로만 기다린다(`ScriptDocumentsBehavior._requested`) - 늦게 오는 `DockItemActivated` 와 핑퐁이 났다.
  - 기본 배치를 바꾸면 `DockLayoutVersion` 을 올린다. 도구 모음 자리는 `BarLayout` 설정. 미리보기 | 문서 나누기는 `DesignSplitGroup`(`SetSplit`·`SwapPanes`).
- **녹화**(`CaptureMonitorViewModel.Recording.cs`, 어댑터 `IVideoRecorder`·`MediaFoundationVideoRecorder`·`VideoRecorderFactory`): 프로젝트 `Recordings\*.mp4`(`ProjectPaths.Recordings`), H.264.
  **fps 는 화면캡처 fps 콤보 값**이고 더 빨리 오는 프레임은 솎는다(세션을 나눈 다른 화면이 더 높은 fps 를 원하면 세션이 그 fps 로 돈다). 비트레이트는 30fps 8Mbps · 60fps 12Mbps(`DefaultBitrate`).
  캡처 스레드는 픽셀만 복사해 넘기고 쓰기 스레드가 SinkWriter 를 첫 프레임 크기로 만들어 쓴다(0.2초어치 넘게 밀리면 버린다 - 1080p 30·60fps 실측 버림 0).
  리드백을 알아서 켜는데 그때 세션이 다시 시작되므로 **녹화기는 그 뒤에** 만든다. 입력은 RGB32 + 양수 `DefaultStride`(안 적으면 뒤집힌다), 크기는 짝수로 자른다.
  캡처가 멈추거나 크기가 바뀌면 파일을 닫는다. 길이 제한은 없고 디스크가 한도다.
  **한 녹화 = 한 파일, 조각 mp4**(`MFCreateFile` + `MFCreateFMPEG4MediaSink` + `MFCreateSinkWriterFromMediaSink`) - 쓰는 도중에도 조각(moof)이 디스크에 붙어 **앱이 죽어도 그때까지는 튼다**
  (사용자 요구 "10초·1분마다 확정"). 싱크를 직접 만들면 SinkWriter 가 싱크를 안 내린다 - 싱크 `Shutdown`·바이트 스트림 `Close` 는 우리가.
  **조각 간격은 싱크가 정하고 키프레임과 무관**(실측 2026-09-15): 약 7~9장마다(1080p 30fps 0.25초 · 60fps 0.13초), 키프레임은 [0,9,90]. `MaxKeyframeSpacing` 은 1초·10초가 바이트까지 같아 안 먹는다.
  그래서 간격 콤보(`나눠 쓰기`/`RecordingSegment`)는 **없앴다** - 요구보다 촘촘히 확정되고, 효과 없는 콤보는 되는 줄 알게 만든다.
  검사 `--vision` 의 녹화 줄: 쓰는 도중 파일을 떠 둔 사본을 MF 로 풀어 프레임을 읽는다(40장→36 · 75장→72), 닫으면 75/75.
- **영상을 캡처 대상으로**(사용자, 2026-09-15 - 게임 없이 몹 찾기·글자 읽기·스크립트 흐름 시험): 대상 콤보 맨 아래 `[영상] 파일.mp4`(프로젝트 `Recordings`, 새것부터).
  `CaptureTargetKind.Video` + `CaptureTarget.FilePath`, 구현 `VideoFileCaptureSession`(SourceReader 고급 영상 처리 → RGB32 → 위에서 아래·알파 255 로 고쳐 D3D 텍스처 + 리드백 픽셀).
  녹화 속도대로 흐르고 끝나면 되감는다, fps 솎기는 WGC 와 같은 90% 규칙. 핸들이 모두 0 이라 허브·선택 복원은 `CaptureTarget.Key`(영상은 경로)로 가른다.
  **자리는 영상 픽셀**(`CaptureTargetBounds` 가 (0,0,w,h), 크기는 `TryGetFrameSize` 캐시) - 몹 좌표가 그대로 나온다.
  **입력은 절대 안 나간다**: 미리보기는 `PreviewInputRouter` 가 `InputForwardResult.VideoTarget`(안 막으면 영상 좌표로 진짜 화면 왼쪽 위를 누른다),
  실시간 스크립트는 `LiveScriptSession.BuildHost` 가 `InputAdapterFactory.CreateSilent()`(부른 것은 호출 로그에만)로 바꾸고 조준 배율 학습을 끈다(화면이 안 돌아 배율이 끝없이 커져 저장된다).
  검사 `--vision` 의 `영상 대상:` 줄(1080p 크기·방향·속도·되감기·솎기·허브·입력 막기).
- **학습하는 동안 몹 찾기는 GPU 모델을 내려놓는다**(`TrainingActivity`) - 3GB 카드에서 캡처·DirectML 추론·CUDA 학습이 겹쳐 GPU 가 리셋됐다(실측). 리셋 뒤에는 앱을 다시 켜야 해 그렇게 알린다(`IsGpuLost`).
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
- **정적 이벤트**(`SolutionWorkspace.Changed`·`LightweightThemeManager.CurrentThemeChanged` 등)는 닫힐 때 반드시 푼다.

## 외부와 닿는 것은 어댑터로

OS·하드웨어·외부 라이브러리는 **인터페이스 + 구현 + 팩터리**. 구현이 하나여도 인터페이스를 만들고, 새 구현은 팩터리만 고치고, 인터페이스에 `Name` 을 둔다.

| 인터페이스 | 구현 | 고르는 곳 |
|---|---|---|
| `IScreenCaptureAdapter` | `WgcCaptureSession` · `VideoFileCaptureSession`(영상) | `ScreenCaptureAdapterFactory` · `SharedCaptureHub` |
| `IInputAdapter` | `SendInputAdapter` · `WindowMessageInputAdapter` · `InterceptionInputAdapter` | `InputAdapterFactory` |
| `IUiAutomationAdapter` | `WindowsUiAutomationAdapter` | `UiAutomationAdapterFactory` |
| `IGlobalHotkeyAdapter` | `GlobalHotkeyAdapter` | `GlobalHotkeyAdapterFactory` · `SharedHotkeysFactory` |
| `IWindowTargetAdapter` | `Win32WindowTargetAdapter` | `WindowTargetAdapterFactory` |
| `IOcrEngine` | `WindowsOcrEngine` | `OcrEngineFactory` |

- **능력이 경로마다 다르면 능력별 인터페이스**(`IScanCodeInput` SendInput·Interception / `ICharacterInput`·`IImeControl` PostMessage·SendMessage). 부르는 쪽이 `adapter is IXxx` 로 묻는다 -
  늘 false 인 빈 메서드는 되는 줄 알고 쓰게 만든다.
- `CreateWithFallback` 은 다른 경로로 내려앉되 `FellBackFrom`·`Reason` 을 돌려준다 - 조용히 다른 길로 보내지 않는다.
- **창 메시지 경로**(`WindowMessageInputAdapter` = PostMessage + `SendMessageTimeout`, 맨 `SendMessage` 금지):
  - 글자는 반드시 `WM_CHAR` - 키로 보내면 부친 Shift 가 안 먹어 `abC!` → `abc1`.
  - P/Invoke 는 **W 판을 명시**(`PostMessageW`) - ANSI 판이 잡히면 한글이 `?`.
  - 한/영 은 `WM_IME_CONTROL`(변환 모드). `WM_INPUTLANGCHANGE`(자판)와 다르다.
  - **게임에는 안 먹는다**(Raw Input·DirectInput) - 게임은 SendInput 이나 Interception. 보낼 창은 `IWindowTargetAdapter` 로 고른다.
- 입력 어댑터는 `Input/`, `Capture/Input/` 에는 미리보기 좌표 계산만(`PreviewInputRouter` 등). 게임 입력 함정은 `docs/실시간-스크립트.md`.

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
    검사 `VerticalAlignmentCheck`(`--labeling-screen`·`--script-screen`·`--environment-screen`) - 화면을 찍는 검사를 새로 만들면 넣는다.
  - 칸 옆 버튼은 `dxe:ButtonEdit` 안에 넣고 `AllowDefaultButton="False"` 를 같이 적는다(안 적으면 빈 `…` 버튼이 하나 더 붙는다). 그림 버튼은 `GlyphKind="User"` + `Image`(가운데 정렬).
  - `dxlc` 에는 `ItemHeight`·`ShowLabel` 이 없다 - 라벨을 비우려면 `AddColonToLabel="False" Label=""`.
- 폰트·크기는 `BaseFontFamily`/`BaseFontSize` 를 **DynamicResource** 로. 경량 테마(`UseLightweightThemes`)라 표준 WPF 컨트롤은 테마를 안 탄다.
- 그리드는 `BaseGridControl`/`BaseTableView`.
  - 행 번호(`IsRowNumber`)를 쓰면 `IndicatorWidth` 를 숨은 `Border`(`SharedSizeGroup="RowNumberGroup"`)의 폭으로 되돌린다(`CaptureMonitorView.xaml` 예시) - 안 하면 세 자리부터 잘린다.
  - `IsColumnAutoWidth` 를 켜면 열 너비가 Auto 라 사용자가 끈 너비가 안 남는다.
  - 저장된 배치의 `ActualShowSearchPanel` 은 복원 전에 지운다. 하네스도 `DataControlBase.AllowInfiniteGridSize = true`.
- `ItemsSource` 가 있는 콤보는 **`SelectedItem`**(`Mode=TwoWay, UpdateSourceTrigger=PropertyChanged`)으로 묶는다 - `EditValue` 면 화면과 실제 동작이 어긋난다.
- DevExpress 내장 SVG(`dx:DXImage`)는 없는 경로면 런타임에 터진다 - 아이콘은 확인된 Axialis(`image:FreeImage`)를 쓴다.
- ViewModel 사이는 `MessengerUtility`. 예외 표시는 `ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName())`.
- **DirectML 네이티브**는 `Libs/DirectML/x64/` 에서 나간다(패키지는 `ExcludeAssets="all"`). 올릴 때 새 패키지의 DLL 로 덮고 같이 커밋 - onnxruntime 과 짝이 안 맞으면 세션 만들 때 터진다.
  `DirectML.Debug.dll` 은 Debug 구성만(csproj 에서 나눈다). `.pdb` 는 저장소에 안 둔다.
- **Interception**: `interception.dll`·`install-interception.exe`(같은 v1.0.1)는 `Minguk.Tools/Native/x64/`(x86 이면 `BadImageFormatException`).
  상태는 `keyboard`·`mouse` 서비스로 읽고, 설치는 별도 프로세스 `runas`(앱 전체를 관리자로 올리지 않는다), 설치 뒤 **재부팅**.
- `Minguk.Image` 는 솔루션에 없다 - `Libs/Minguk.Image.dll` 을 HintPath 로 문다(리소스 4만 개). 아이콘을 바꾸면 그 프로젝트를 Release|x64 로 따로 빌드해 덮고 커밋.

## 검증

`Minguk.Tools.Tests` 는 단위 테스트가 아니라 **실행형 하네스**다(`Minguk.Tools.Tests/README.md`). 앱이 켜져 있으면 출력 복사가 잠겨 빌드가 실패한다(컴파일 오류와 구별).

```
dotnet run --project Minguk.Tools.Tests -c Debug -- --views               # 화면·모듈 생성 (안전)
dotnet run --project Minguk.Tools.Tests -c Debug -- --vision              # 라벨·검출·OCR·스크립트·빌드·조준(가짜 어댑터) (안전)
dotnet run --project Minguk.Tools.Tests -c Debug -- --solution            # 솔루션·공유 프로젝트·탐색기·▶ 찾기 (안전, 임시 폴더)
dotnet run --project Minguk.Tools.Tests -c Debug -- --script-screen       # 스크립트 화면 화면 밖 띄우기·바인딩 오류·정렬·PNG (안전)
dotnet run --project Minguk.Tools.Tests -c Debug -- --labeling-screen     # 라벨링 화면 패널 높이·정렬·PNG (안전)
dotnet run --project Minguk.Tools.Tests -c Debug -- --environment-screen  # 환경설정 화면 두 칸·아이콘·정렬·PNG (안전)
dotnet run --project Minguk.Tools.Tests -c Debug -- --detect-check        # 모델이 라벨을 다시 찾는지(재현율)
dotnet run --project Minguk.Tools.Tests -c Debug -- --backend=SendInput   # 입력 경로 전체 (커서·키보드를 가져간다)
dotnet run --project Minguk.Tools.Tests -c Debug -- --canvas-drag         # 라벨 캔버스 실제 마우스 끌기 (커서)
```

- 화면(XAML)을 고쳤으면 `--views` - XAML 은 빌드를 통과하고 런타임에 터진다. 그 화면을 찍는 검사가 있으면 그것도.
- 라벨·검출·스크립트만 고쳤으면 `--vision`. 솔루션·프로젝트·작업 공간이면 `--solution`.
- 입력 쪽을 고쳤으면 **세 경로 모두**(`--backend=`). 한글은 메모장으로도 쳐 본다(하네스 대상이 WPF 라 IME 문제를 못 잡는다).
- 창(MainWindow)은 하네스가 안 만든다 - 제목 표시줄을 고쳤으면 앱을 잠깐 띄워 본다.
