DevExpress WPF 컨트롤, WPF 개발자

## 이 프로젝트

TamsTools 의 셸 구조(MainWindow / MainView / MainViewModel / MainMenu)와 기준 스타일(Minguk.Base)을
그대로 가져온 새 프로젝트. 도메인 코드(TAMS · Redis · XPO · MCP)는 들어 있지 않다.

## 화면 하나 추가하는 법

1. `Views/XxxView.xaml` + `.xaml.cs` (UserControl)
2. `ViewModels/XxxViewModel.cs` — `DocumentViewModelBase` 상속, `Create()` 팩터리, 생성자에서 `Caption`/`CaptionImage` 지정
3. `App.xaml.cs` 의 `builder.Services.AddTransient<XxxView>()` 에 등록
4. `Source/MainMenu.cs` 에 `MenuItemModel.Create(...)` 추가 (`class_nm` 은 View 의 전체 타입 이름)

3번을 빠뜨리면 메뉴는 보이지만 탭이 비어서 열린다 — `MainViewLocator` 가 DI 에서 뷰를 꺼내기 때문이다.

## 화면 셋: 캡처 · 스크립트 · 플레이

창을 잡는 화면이 셋이다(2026-09-11 에 캡처 모니터 하나를 나눴다). 한 화면에 담기·검출·OCR·스크립트를
전부 얹으니 도구 줄이 넘쳤고, 플레이만 하는 PC 에 편집기·담기가 딸려 갔다.

| 화면 | ViewModel | 하는 일 |
|---|---|---|
| 캡처 | `CaptureMonitorViewModel : CaptureViewModelBase` | 순수 캡처. 대상·fps·미리보기·프레임 저장·데이터셋에 담기(F8)·통계 표 |
| 스크립트 | `ScriptStudioViewModel : RecognizingCaptureViewModelBase` | 몹 찾기·추적·글자 영역·글자 읽기·이름표 읽기를 보면서 스크립트를 쓰고 한 번씩 돌린다. 담기(F8)도 된다 - 찾은 것이 라벨로 들어간다 |
| 플레이 | `PlayViewModel : RecognizingCaptureViewModelBase` | 게임 연결, 미리보기 켜고 끄기, Scripts 폴더의 스크립트를 골라 1회(F5)·반복(F6). 편집 없음 |

- **바탕 둘.** `CaptureViewModelBase`(잡기·미리보기·입력 전달·저장·담기·1초 통계) 위에
  `RecognizingCaptureViewModelBase`(`.Detect.cs` 찾기 · `.Ocr.cs` 글자). 캡처 화면은 첫 바탕만 쓴다 -
  담기만 하는 화면에 68MB 모델과 libtorch GPU 메모리를 물릴 이유가 없다.
- 바탕이 열어 둔 자리: `OnFramePixels(e)`(캡처 스레드, 기다리면 안 됨) · `TryInterceptPreviewMouseDown` ·
  `OnPreviewMouseMove/Up` · `DetectionsForLabels` · `SupportsCollecting` · `OnStatisticsRow` · `OnRunningStateChanged`.
  파생 화면은 `RestoreSettings/SaveSettings/ReleaseResources` 에서 반드시 `base` 를 부른다.
- 바탕 이름은 `...Base` 로 끝난다. `DocumentViewModelBase` 가 로거·뷰 이름을 "ViewModel 로 끝나는 첫 타입" 에서
  구하므로, 바탕 이름이 `ViewModel` 로 끝나면 세 화면이 한 이름으로 찍힌다.
- **설정 키는 파생 화면 이름으로** 저장된다(`AppSettingUtility` 가 실제 타입을 쓴다). 그래서 캡처와 플레이가
  대상 창·fps 를 각자 기억한다. 나누기 전 값(`CaptureMonitorViewModel.*`)은 `GetSettingOrLegacy` 가 처음 한 번
  물려준다 - 대상 창·문턱·추적·글자 영역·OCR 언어.
- **미리보기 판과 입력 전달 도구 줄은 `Views/Parts` 의 UserControl** 이다(`CapturePreviewPanel` · `PreviewForwardBar`).
  DataContext 를 물려받아 바탕의 커맨드에 묶이고, `UIObjectService` 이름(`PreviewImageObjectService` 등)도 그 안에
  있다. 겹그림(`DetectionOverlay`)은 판의 `Overlay` 에 화면이 얹는다 - 캡처 화면은 없는 프로퍼티에 묶이지 않게.
- **스크립트 문서는 `ScriptWorkbench`, 실행은 `ScriptPlayer`.** 스크립트·플레이·입력 자동화 셋이 같이 쓴다.
  입력 자동화 화면에 남은 스크립트 코드는 본보기 줄 끼우기(`DoAddStep`)뿐이다.
  스크립트 입력은 미리보기 입력 전달과 **같은 어댑터**(`_inputRouter.InputAdapter`)로 나가고, 보내기 직전에
  `TryFocusTargetWindow` 로 대상 창을 앞으로 가져온다.
- 전역 단축키 소유: F8 담기 = 캡처·편집(`SupportsCollecting`), F5/F6 = 플레이와 입력 자동화(같이 열면 나중 것이 실패,
  상태에 적힌다). 두 화면이 같은 키를 쥐면 안 되는 이유가 이것이다.
- 각 화면이 **제 캡처 세션**을 든다. 캡처 화면과 플레이 화면을 같이 켜면 같은 창을 두 번 잡는다 - 공유 세션과
  스크립트가 읽는 인식 허브는 `docs/스크립트-설계.md` 2단계.
- 검증: `--views` 가 세 화면을 만들고 메뉴 아이콘을 본다. 실시간은 `screens.ps1`(세 화면 차례로)·`live-game.ps1`(스크립트 화면에서 몹 찾기).

## ViewModel 작성 규칙

`DocumentViewModelBase` 가 생명주기·서비스·예외를 다 들고 있다. 화면은 필요한 단계만 채운다.
(`ViewModels/DashboardViewModel.cs` 가 최소 예시다.)

### 파일 분리

| 파일 | 담는 것 |
|---|---|
| `XxxViewModel.cs` | 생명주기 — `Create()` / 생성자 / `Initialize*` / `Save*` / `Release*` |
| `XxxViewModel.Model.cs` | 상태 — 바인딩 프로퍼티, 커맨드 선언, 컨트롤 참조 |
| `XxxViewModel.Code.cs` | 동작 — 커맨드가 실제로 하는 일 (길어질 때만 만든다) |

### 초기화 순서

View 의 `Loaded` 와 부모(MainViewModel) 주입이 **모두** 도착하면 한 번만 돈다.

```
InitializeControls()   컨트롤 참조 확보  (FindControl<T>("...ObjectService"))
InitializeObservable() 이벤트 구독       (만든 구독은 Disposables 에 넣는다)
RestoreSettings()      저장값 복구       (GetSetting)
OnLoaded()             실제 진입 작업    (데이터 조회 등)
```

닫힐 때는 `OnClose(e)` → `OnDestroy()` → `SaveSettings()` → `ReleaseResources()` → 구독/메신저 해제.

- 생성자에는 **XAML 이 바인딩할 대상만** 만든다(커맨드·컬렉션·Caption). 화면이 떠 있어야 되는 일은 전부 `Initialize*` 단계로.
- `OnInitializedCommand` / `OnClosingCommand` / `DoCloseCommand` 는 베이스가 준다. 화면에서 다시 선언하지 않는다.
- 화면 안에서 `NLog.LogManager.GetCurrentClassLogger()` 를 새로 만들지 않는다. 베이스의 `Logger` 를 쓴다 (POCO 프록시가 아니라 실제 ViewModel 이름으로 찍힌다).

### 예외

`try/catch` 를 직접 쓰지 말고 `Guard(...)` 로 감싼다. NLog 기록 + `ExceptionViewer` 표시까지 한 번에 된다.

```csharp
private void DoRefresh() => Guard(() => { ... });
```

복구 동작이 필요한 곳(실패 시 세션을 되돌리는 등)만 `try/catch` 를 직접 쓴다.

## 외부와 닿는 것은 어댑터로

OS·하드웨어·외부 라이브러리에 닿는 코드는 **인터페이스 + 구현 + 팩터리** 세 조각으로 나눈다.
화면과 ViewModel 은 인터페이스만 알고, 구현이 바뀌어도 손댈 것이 없게 한다.

지금 있는 것:

| 인터페이스 | 구현 | 고르는 곳 |
|---|---|---|
| `IScreenCaptureAdapter` | `WgcCaptureSession` | `ScreenCaptureAdapterFactory` |
| `IInputAdapter` | `SendInputAdapter` · `WindowMessageInputAdapter` · `InterceptionInputAdapter` | `InputAdapterFactory` |
| `IUiAutomationAdapter` | `WindowsUiAutomationAdapter` | `UiAutomationAdapterFactory` |
| `IGlobalHotkeyAdapter` | `GlobalHotkeyAdapter` | `GlobalHotkeyAdapterFactory` |
| `IWindowTargetAdapter` | `Win32WindowTargetAdapter` | `WindowTargetAdapterFactory` |

입력 어댑터는 `Input/` 에 있다. `Capture/Input/` 에는 미리보기 좌표 계산만 남는다
(`PreviewInputRouter`, `PreviewInputMapper`, `CaptureTargetBounds`, `InputForwardResult`).
입력은 캡처 전용이 아니라서 나눠 두었다.

능력이 경로마다 다른 것은 **능력별 인터페이스**로 뺀다. 부르는 쪽이 `adapter is IXxx` 로 물어보고,
아니면 못 한다고 말한다. 못 하는 것을 늘 false 만 돌려주는 빈 메서드로 두면 부르는 쪽이
되는 줄 알고 쓰기 때문이다.

| 인터페이스 | 있는 경로 | 무엇 |
|---|---|---|
| `IScanCodeInput` | SendInput · Interception | 스캔코드 |
| `ICharacterInput` | PostMessage · SendMessage | `WM_CHAR`. 글자를 그대로 넣는다 |
| `IImeControl` | PostMessage · SendMessage | `WM_IME_CONTROL`. 대상 IME 의 한/영 을 직접 바꾼다 |

**창 메시지 경로는 영문·숫자·문장부호·Enter·한글을 다 넣고, 한/영 전환도 한다.**
글자는 IME 를 거치지 않고 완성된 음절을 그대로 주고(`WM_CHAR`), 한/영 은 대상 창의 기본 IME
윈도우에 `WM_IME_CONTROL` 을 보내 바꾼다. 키를 흉내 내는 것이 아니라 IME 에게 직접 말한다.
못 하는 것은 진짜 커서를 움직이는 것과, 창 메시지를 안 보는 대상(WPF·WinUI 의 마우스 입력 등)이다.

`WM_INPUTLANGCHANGE` 와 헷갈리지 않는다. 그쪽은 **입력 언어**(자판)를 바꾸는 것이고,
한/영 은 그 언어 안에서의 **변환 모드**다.

창 메시지를 넣는 경로는 **두 가지로 돈다.** `WindowMessageInputAdapter` 하나가 `PostMessage`
(부치고 곧바로 돌아온다)와 `SendMessage`(`SendMessageTimeout` 으로 처리될 때까지 기다린다)를
겸한다. 넣는 메시지가 같고 건네는 방식만 다르므로 구현을 나누지 않았다.
맨 `SendMessage` 는 쓰지 않는다 — 대상이 멈춰 있으면 부르는 쪽이 영영 굳는다.

그 경로에서 글자는 반드시 `WM_CHAR` 로 넣고, 두 가지를 지킨다.

- 키로 보내면 안 된다 — 부친 `VK_SHIFT` 는 대상 스레드의 키 상태를 바꾸지 못해서
  `abC!` 가 `abc1` 로 들어간다(실측).
- **`PostMessageW`** 여야 한다. 이름만 `PostMessage` 로 P/Invoke 하면 ANSI 판이 잡혀
  한글이 `?` 로 뭉개진다(실측). 창 메시지를 부르는 P/Invoke 는 전부 W 판을 명시한다.

스캔코드로 키를 넣는 것은 `IScanCodeInput` 으로 따로 뺐다. PostMessage 경로는 그것을
제대로 할 수 없고(대상 IME 가 그렇게 온 한/영 전환을 받지 않는다), 못 하는 것을 늘 false 만
돌려주는 빈 메서드로 두면 부르는 쪽이 되는 줄 알고 쓰기 때문이다.
필요한 쪽은 `adapter is IScanCodeInput` 으로 물어보고, 아니면 못 한다고 말한다.

`PostMessage` 는 **게임에는 안 먹는다.** 오버워치 창으로 `WM_MOUSEMOVE → WM_LBUTTONDOWN →
WM_LBUTTONUP` 이 좌표 변환까지 맞게 나갔고 오류도 없었는데(로그 12:13) 게임은 꿈쩍도 안 했다.
DirectX 게임은 마우스를 창 메시지가 아니라 Raw Input(`WM_INPUT`)·DirectInput 으로 읽는다.
포커스를 안 뺏는 장점은 메모장·브라우저 같은 보통 프로그램에서만 산다. 게임은 `SendInput`
(포커스 필요, 대상이 관리자면 이 앱도 관리자)이나 Interception 드라이버(커널) 경로다.

`PostMessage` 는 **보낼 창을 알아야 한다.** 진짜 커서를 안 움직이므로 "포커스를 가진 창" 에
기댈 수 없다. 안 정해 주면 `WindowFromPoint` 로 마지막 좌표 아래 창에 보내는데, 나가긴
나가지만 어디로 갔는지 알 수 없다 - 그래서 `IWindowTargetAdapter` 로 고르게 한다.

`InputAdapterFactory.CreateWithFallback` 은 고른 경로를 못 쓸 때 다른 경로로 내려앉되
`FellBackFrom` · `Reason` 을 함께 돌려준다. 조용히 다른 길로 보내면 안 된다 - 화면에 적는다.

지키는 것:

- 구현이 하나뿐이어도 인터페이스를 만든다. 둘째가 필요해질 때 부르는 쪽을 안 고치려는 것이다.
- 새 구현을 넣을 때 **팩터리만** 고친다. 부르는 쪽이 구체 타입을 알면 어댑터로 나눈 뜻이 없다.
- 인터페이스에 `Name` 을 둔다. 지금 어느 경로로 도는지 로그·화면에 보여 줄 수 있어야 한다.
- 돌아가는 중에 갈아끼울 수 있으면 그렇게 만든다 (`PreviewInputRouter.InputAdapter` 처럼).

## 입력 시퀀스

세 겹이다. 겹마다 사는 이유가 다르다.

| 겹 | 무엇 | 왜 |
|---|---|---|
| `SequenceScript` | 사람이 읽고 쓰는 **글** | 복사·되돌리기·찾아 바꾸기가 편집기에서 공짜로 온다 |
| `SequencePlan` · `SequenceStepDefinition` | 저장·편집되는 **데이터** | 경로를 고르기 전에도 적어 둘 수 있다 |
| `InputSequence` · `InputStep` | 실제로 도는 **델리게이트** | 서비스를 물고 있어 저장할 수 없다 |

화면이 들고 있는 원본은 **글**이다. 글이 바뀔 때마다 계획으로 읽고, 돌릴 때
`plan.Build(service, holdMs)` 로 시퀀스를 만든다. 경로를 바꿔도 글과 계획은 그대로다.

- `SequenceStepKind` 는 **늘리기만** 한다. 이름을 바꾸거나 빼면 예전에 적어 둔 것을 못 읽는다
  (JSON 으로 저장할 때도 숫자가 아니라 **이름**으로 적는다 - 열거형 순서를 바꿔도 뜻이 안 변하게).
- 명령 이름을 늘리면 `Resource/SequenceScript.*.xshd` 두 파일의 강조 규칙도 같이 고친다.
- 틀린 줄을 만나도 파싱을 멈추지 않는다. 첫 오류에서 그만두면 열 줄 틀렸을 때 열 번을 돌아야 한다.
  틀린 줄만 빼고 나머지는 계획에 담되, **실행은 막는다** - 반쪽짜리 시퀀스가 나가는 것이 더 나쁘다.

시퀀스를 굳히는 시점은 **시작할 때 한 번**이다. 도는 중에 글을 고쳐도 그 바퀴는 영향받지 않는다.
전송은 `Task.Run` 으로 UI 스레드에서 떼어 낸다.

### 스크립트 엔진

무엇을 보낼지는 **스크립트**로 쓴다. 언어는 세 가지고, `IScriptEngine` + 구현 + 팩터리다.

| 언어 | 구현 | 받아 오는 것 |
|---|---|---|
| C# (기본) | `RoslynScriptEngine` | 없음 |
| JavaScript | `JavaScriptEngine` (Jint) | 없음 — 순수 .NET |
| Python | `PythonScriptEngine` (Python.NET) | 첫 선택 때 임베더블 CPython 11MB |

**스크립트를 돌려도 입력은 나가지 않는다.** 부르는 것은 `SequenceScriptApi` 고, 그것은 단계를
적어 둘 뿐이다(**녹화 방식**). 그래서 순서 미리보기·시작 전 대기·반복·중지가 그대로 살아 있고,
언어를 바꿔도 그 뒤는 손댈 것이 없다. `for` 문은 단계로 풀려 나온다.

못 하는 것: 실행 **도중**에 반응하는 것("화면에 X 가 보이면"). 화면을 읽는 단계 자체가 아직
없어서 지금은 잃는 것이 없다. 필요해지면 그때 갈래를 하나 더 둔다.

- API 를 늘리면 **네 곳**을 같이 고친다 — `SequenceScriptApi`, `PythonScriptEngine.BoundNames`,
  `JavaScriptEngine.Bind`, 그리고 구문 강조 `Resource/SequenceScript.*.xshd`.
- 이름은 영문·한글 둘 다 연다(`Type` / `글자`). 세 언어가 **같은 이름·같은 인자**를 쓴다 -
  문법만 다르고 되는 일이 같아야 오갈 수 있다.
- **Roslyn 은 컴파일할 때마다 어셈블리를 만들고 그것은 언로드되지 않는다.** 글자마다 컴파일하면
  쌓이기만 하므로 타이핑을 500ms 묶었다가 한 번만 돌리고, 같은 글이면 캐시를 쓴다.
  컴파일한 것은 10분간 붙들고 있다가 안 쓰이면 놓는다(`RoslynScriptEngine.KeepAlive`).
  "GC 를 막는" 것이 아니라 참조를 들고 있는 것이다 — `GC.TryStartNoGCRegion` 은 여기 쓸 물건이 아니다.
- **파이썬은 한 프로세스에 한 번만 켠다.** `PythonEngine.Shutdown()` 뒤 재초기화가 깨지므로
  화면을 닫아도, 언어를 바꿔도 켜 둔 채로 둔다. 파이썬 객체를 만지는 곳은 전부 `Py.GIL()` 안이다.
- pythonnet 에 열거형을 열 때 `typeof(T).ToPython()` 은 안 된다. 파이썬 쪽에서 `RuntimeType` 으로
  보여 멤버가 안 잡힌다. 값을 하나씩 심고 묶는 껍데기를 파이썬에서 만든다.

### 스크립트 파일

글은 **설정에도 남고 파일로도 남는다.** 둘은 서로를 대신하지 않는다 — 설정에 남는 것은
"지난번에 쓰던 것"이라 앱을 껐다 켜도 그대로 나오고, 파일은 여러 개를 두고 갈아 끼우는 것이다.
그래서 저장하지 않고 닫아도 잃는 것이 없다. 파일 이름 옆의 `*` 는 "잃는다"는 경고가 아니라
**파일과 지금 글이 다르다**는 표시다.

`Input/Scripting/ScriptFiles` 가 확장자·필터·기본 폴더를 든다.

| 언어 | 확장자 | 또 여는 것 |
|---|---|---|
| C# | `.csx` | `.cs` |
| Python | `.py` | `.pyw` |
| JavaScript | `.js` | `.mjs` |

- **`.cs` 가 아니라 `.csx` 다.** 여기 든 것은 클래스가 아니라 곧바로 도는 문장들이라
  컴파일러가 보는 것이 다르다. Roslyn 스크립팅의 관례를 따른다.
- **확장자마다 언어를 나누는 이유**는 파일만 보고 무엇인지 알기 위해서다. 하나로 쓰면
  열 때마다 사람이 언어를 다시 골라야 하고, 잘못 고르면 문법 오류만 잔뜩 뜬다.
- 열 때는 **언어를 먼저 바꾸고 글을 넣는다.** 순서가 반대면 새 글을 예전 언어로 한 번 돌려
  헛된 오류가 화면에 스쳤다 사라진다.
- 모르는 확장자면 언어를 **그대로 둔다**. 아무거나 고르면 사용자는 글이 틀린 줄 알지
  언어가 어긋난 줄은 모른다.
- **언어를 바꿔도 쓰던 글은 그대로 둔다.** 잘못 골랐을 때 되돌릴 방법이 없어지면 안 된다.
  새 언어로 안 되는 글이면 "고칠 줄" 에 그대로 뜬다.
  다만 **아직 손대지 않은 본보기**는 지울 것이 없으므로 새 언어 본보기로 갈아 끼운다 -
  안 그러면 파이썬을 골라 놓고 C# 본보기를 보게 되고, 그대로 저장하면 C# 이 든 .py 가 나온다.
  갈아 끼우는 조건은 **열어 둔 파일이 없고** "지금 글 == 예전 언어의 본보기" 일 때뿐이다.
  파일을 열어 둔 채면 그 글은 본보기가 아니라 그 파일의 것이다.
- 파일 이름 옆의 `*` 는 **열어 둔 파일이 있을 때만** 붙는다. 견줄 파일이 없는데
  "다르다" 고 할 수 없다.
- 쓸 때는 **UTF-8(BOM 없이)**. 한글 이름을 열어 두고 ANSI 로 쓰면 다른 PC 에서 깨진다.
- 대화 상자는 `dxmvvm:OpenFileDialogService` / `SaveFileDialogService` 를 화면에 두고
  ViewModel 에서 `OpenFileDialogService` · `SaveFileDialogService` 로 받는다.
- **읽기 전용 속성을 `EditValue` 에 묶을 때는 `Mode=OneWay` 를 적는다.** `EditValue` 는
  기본이 TwoWay 라 세터 없는 속성이면 화면이 만들어질 때 `XamlParseException` 으로 터진다
  (`ScriptFileLabel` 로 겪었다). 화면 생성 하네스(`--views`)가 이것을 잡아낸다.

**스크립트는 언어별로 따로 기억한다** (`Script.CSharp` · `Script.Python` · `Script.JavaScript`).
하나만 들고 있으면 파이썬으로 바꿔 놓고 C# 글을 보게 되고, 그 상태로 저장하면 C# 이 든 .py 가
나온다 - 실제로 "파이썬 골랐더니 스크립트는 C#" 이었다. 언어를 바꾸면 쓰던 글을 제 언어 칸에
넣고(`StashShownScript`) 새 언어의 글을 꺼낸다. 손대지 않은 본보기는 안 넣는다 - 넣어 두면
본보기가 바뀌어도 옛 본보기가 되살아난다. 옛 키(`Script`)는 언어별 키가 없을 때만 읽는다.

### 편집기

`AvalonEdit`(`ICSharpCode.AvalonEdit`)을 쓴다. **AvaloniaEdit 이 아니다** - 그쪽은 Avalonia 용
포팅이라 WPF 에 올라가지 않는다.

- `TextEditor.Text` 는 의존 속성이 아니다. `Markup/AvalonEditText` 붙임 속성으로 양방향을 잇는다.
- 순수 WPF 컨트롤이라 **경량 테마가 손대지 않는다.** 배경·글자색을 직접 주지 않으면
  테마를 바꿔도 이 편집기만 그대로 남는다. `Helper/SequenceScriptHighlighting` 이 색을 든다.
- 색은 **경량 테마 팔레트에서 읽는다** — `LightweightThemeManager.CurrentTheme.Palette` 의
  `Brush.Editor.Background` · `Brush.Foreground` · `Brush.Border`. (TamsTools 의
  `SearchTermTheme.Brush` 가 같은 길을 쓴다.) 줄 번호는 팔레트에 없어 본문과 바탕을 섞어 만든다.
- **테마 이름으로 가르지 않는다.** 팔레트로 만든 테마(VS2019Blue)는 이름에 Dark 도 Black 도
  없어 밝은 쪽으로 잘못 본다. XAML 에서 테마 **키**를 짚는 것도 아니다 — 키 이름이 판마다
  달라진다(`LayoutControlThemeKey` 를 짚었다가 MC3072/MC3074 로 막힌 적이 있다).
  강조 벌(낱말·글자 색)만은 팔레트에 없으므로 **팔레트에서 읽은 바탕색의 밝기**로 고른다.
- 팔레트를 못 읽을 때만 이름(`Dark`·`Black`)과 레지스트리(`AppsUseLightTheme`)로 어림한다.
- 테마가 바뀌면 `LightweightThemeManager.CurrentThemeChanged` 로 다시 읽는다.
  **정적 이벤트라 화면이 닫힐 때 반드시 푼다.**
- 전환 자체는 검증 하네스로 못 잰다. `LightweightThemeManager` 는 화면 없이 도는 곳에서
  테마 변경을 안 따라오고(이름을 바꿔도 `CurrentTheme` 이 그대로다) `CurrentTheme` 세터도
  공개가 아니다. 하네스는 "색이 팔레트에서 나오는지"까지만 보고, 전환은 앱에서 눈으로 본다.
- 테마를 바꾸는 곳은 **오른쪽 위 핀 옆의 테마 갤러리**다
  (`MainWindow.xaml` 의 `BarSplitItemThemeSelectorBehavior`). 왼쪽 위 툴바가 아니다 —
  거기는 메뉴·설정·전체화면·재시작·종료다.
- **`TextEditor` 를 바로 얹으면 창 전체의 UI 자동화 트리가 빈다.** 실측으로 갈렸다 -
  같은 화면의 버튼이 그리드 시절 37개에서 편집기를 넣은 뒤 0개가 됐다. 편집기만이 아니라
  창에 있는 모든 것이 안 보인다(스크린 리더에도 그렇다). AvalonEdit 이 만드는 자동화 피어가
  원인이라, `Markup/ScriptEditor` 로 감싸 평범한 `FrameworkElementAutomationPeer` 를 준다.
  **AvalonEdit 을 다른 화면에 또 쓸 일이 생기면 `ScriptEditor` 를 쓴다.**
- 그래도 편집기 안의 **글**은 UIAutomation 으로 읽고 쓸 수 없다 - 밖에서 검증하려면
  좌표로 클릭해 포커스를 준 뒤 키를 보내야 한다.

**빨간 밑줄과 코드 완성** (`Markup/ScriptEditor`)

- 엔진이 준 `ScriptError`(줄 번호)를 `Errors` 로 받아 그 줄 아래에 물결선을 긋는다. 배경 렌더러
  (`IBackgroundRenderer`, Selection 층)라 글을 가리지 않는다. 마우스를 올리면 문장이 풍선으로 뜬다.
  XAML 은 `Errors="{Binding Script.Errors}"`.
- 완성 목록은 `ScriptApiCatalog`(이름·한글 이름·인자·설명 표 하나)에서 나온다. 낱말 첫 글자를 치면 열리고
  Ctrl+Space 로도 연다. 고르면 **이름만** 넣는다 - 괄호까지 넣으면 이미 친 괄호와 겹친다. 세 언어가 같은
  이름을 쓰므로 목록도 하나다. 언어 고유 문법(for·def·let)은 안 한다.
- 파이썬 엔진이 심는 이름은 이 표에서 나온다. 자바스크립트 엔진은 인자 형이 붙은 대리자라 제 손으로 적되,
  `--views` 가 표의 이름을 전부 불러 어긋남을 잡는다. 구문 강조(xshd)는 아직 손으로 맞춘다.
- **오프스크린에서 TextView 의 층은 안 그려진다.** 편집기를 통째로 `RenderTargetBitmap` 에 그려도 밑줄이
  없었다 - 층은 디스패처의 렌더 패스에서만 그려진다. 검사는 렌더러의 `Draw` 를 직접 불러 그 그림만 본다.
  줄 자리(VisualLines)도 편집기가 **진짜 창에 붙어야** 나온다(Measure/Arrange 만으로는 비어 있었다).
  그래서 검사는 화면 밖(-5000,-5000)의 보이지 않는 창에 담아 돌린다. 완성 창도 같은 이유로 그렇게 본다.
- 투명 바탕의 Pbgra32 는 색이 알파로 곱해져 있다. 1.2px 선은 가장자리가 반투명이라 색을 그대로 견주면
  못 찾는다 - 알파로 되돌린 뒤 "붉은가" 만 본다.

### 실시간 모드 (스크립트·플레이 화면)

스크립트·플레이 화면의 스크립트는 **실시간 모드**로 돈다 - 부르면 곧바로 나가고, 화면을 읽을 수 있다.
입력 자동화 화면은 계획 모드(적어 두었다 나중에 보냄) 그대로다. 두 모드는 같은 이름을 쓴다(`ScriptApiCatalog`).

- **실체는 둘**: 계획 `SequenceScriptApi`, 실시간 `Input/Scripting/Live/LiveScriptApi`. 표(`ScriptApiCatalog`)의
  `Mode` 가 어느 쪽에 있는지 말하고, `--vision` 이 리플렉션으로 두 클래스에 표의 이름이 다 있는지 센다.
  실시간 전용: 몹들·가장가까운몹·몹기다리기·읽기·숫자읽기·키·누르기·떼기·중지되었나·출력·보기·끝.
- **엔진마다 두 길**: `RunAsync`(계획: 돌려서 계획을 받음) 와 `CheckLiveAsync`(실시간: 컴파일·문법만) ·
  `RunLiveAsync`(실시간: 끝까지 돌림). 실시간 검사는 돌리면 입력이 나가므로 부작용이 없어야 한다.
  C# 은 전역 타입이 다르므로 캐시도 따로. 파이썬은 `name = api.name` 으로, 자바스크립트는
  `function name(){ return api.name.apply(api, arguments); }` 로 표의 이름을 심는다 - 대리자를 하나씩 안 적는다.
- **끝난 이유는 `LiveScriptApi.Outcome`** 으로 가른다. 중지·F9·끝() 은 오류가 아니라 빈 목록, 안전장치가 막은
  것은 `GuardMessage`, 그 밖은 스크립트 오류. 예외 타입으로 가르면 안 된다 - 파이썬을 거치면 전부
  PythonException 이 된다.
- **안전장치는 전부 `LiveScriptApi` 안에**: 대상 창이 앞에 없으면 입력을 보내지 않고 멈춤(SendInput·Interception 만),
  초당 입력 상한 30(넘으면 기다림), 모든 호출이 중지 토큰을 봄(`쉬기()` 도 토큰으로 기다린다), 눈이 없으면
  `몹들()` 은 빈 목록이 아니라 멈추고 이유를 말함. 실행 시간 상한은 `ScriptPlayer.RunTimeLimitSeconds`(기본 600).
- **비상 정지 F9** 는 `EmergencyStop` 이 스크립트가 도는 동안만 쥔다. 누르면 중지 + 누르고 있던 키 전부 뗌.
  늘 쥐면 다른 화면·프로그램의 F9 를 빼앗는다.
- **인식 허브 `Vision/Perception/IPerceptionHub`**: 인식 베이스가 상태(잡는 중·찾는 중·대상)·검출·이름표를
  올리고, 스크립트가 읽는다. 프레임 픽셀은 `WantsFrames` 일 때만(읽기() 를 부른 뒤) 0.25초마다 복사해 올린다.
  앱에 하나(`PerceptionHubFactory.Default`), 하네스는 가짜 허브를 꽂는다.
- 몹 자리는 **화면 픽셀**로 준다(`ScriptMob`). 비율↔픽셀은 부르는 순간의 대상 창 자리로 API 가 바꾼다.
- `LiveScriptSession`(ViewModels) 이 화면 둘이 같이 쓰는 묶음 - 출력 칸(`ScriptConsole`), F9, API 에 빌려 줄 것.
  `ScriptPlayer` 는 한 바퀴를 `ScriptRunContext.RunOnce` 대리자로 받아 계획·실시간을 가리지 않는다.
- 검증: `--vision` 의 실시간 검사는 **가짜 어댑터**(누른 것을 적기만)와 가짜 허브로 돈다. 입력은 절대 실제로 안 나간다.
  MoveTo 는 부드럽게 여러 걸음이라 "마지막 걸음이 몹 자리" 로 본다.

### 디버그 (스크립트 화면)

- **호출 로그**: `LiveScriptApi` 가 API 를 부를 때마다 `ScriptCall`(시각·이름·인자·결과·걸린 시간)을 `LiveScriptHost.Trace` 로
  넘기고 `ScriptConsole.CallsText` 에 쌓인다(최근 200개). C# 스크립트는 한 줄씩 밟을 수 없어 이것이 디버거 몫이다.
  `쉬기()` 는 500ms 이상만 남긴다 - 반복문이 초당 수십 줄을 만든다.
- **중단점·한 줄씩**은 `ScriptDebugSession` 이 든다. 화면(UI)은 `Breakpoints` 컬렉션을 고치고 계속(F5)·한 줄(F10)을 누르고,
  엔진(스크립트 스레드)은 줄마다 `ShouldBreak` 를 묻고 `Pause` 에서 기다린다. 컬렉션은 UI 것이라 스레드용 집합을 따로
  두고 바뀔 때 갈아 끼운다. 멈춘 채로 중지하면 토큰이 깨워 그 자리에서 끝난다.
- **JavaScript** 는 Jint 디버거(`Options.Debugger.Enabled`, `InitialStepMode=Into`, `Debugger.Step` 이벤트). 사용자 글은
  `Execute(source, "script")` 로 이름을 붙이고 `info.Location.SourceFile` 로 우리 껍데기 함수(shim)와 가른다 - 껍데기 안이면
  `StepMode.Over`. 변수는 `CurrentScopeChain` 의 `BindingNames`/`GetBindingValue`, 우리가 심은 이름과 함수는 뺀다.
- **Python** 은 `sys.settrace`. 사용자 글을 `PythonEngine.Compile(source, "<script>")` 로 이름 붙여 `Execute` 하고, 추적 함수가
  `co_filename == '<script>'` 인 줄에서만 `__dbg.ShouldBreak/Pause`(`PythonDebugBridge`)를 부른다. 멈춘 동안 GIL 을 쥔 채
  기다린다 - 파이썬 스레드가 하나뿐이라 괜찮다.
- **C#** 은 `SupportsStepping=false`. Roslyn 스크립트 어셈블리에는 디버거를 붙일 자리가 없다. 한 줄(F10)을 누르면 상태 줄이 그렇게 말한다.
- 편집기(`ScriptEditor`): 왼쪽 여백(`BreakpointMargin`, AbstractMargin)을 누르면 중단점, Ctrl+B 도 같다. `CurrentLine` 이 0 이
  아니면 그 줄을 노랗게 칠하고 굴린다. 여백은 `OnRender` 에 투명 판을 깔아야 클릭이 온다 - 안 그린 자리는 히트 테스트에 안 걸린다.
- 스크립트 화면 단축키: F5 실행/계속, F10 한 줄, F9 비상 정지(도는 동안). 플레이·입력 자동화도 F5 를 쥐므로 같이 열면 나중 것이 실패한다.
- 검증(`--vision`): 가짜 어댑터로 JS 중단점(2번 줄에서 멈춤·변수 `a=1`·계속하면 끝), 한 줄씩(1→2→3), 멈춘 채 중지, 호출 로그.

## 몹 검출 (이미지 캡처 → 라벨링 → 학습 → 추론)

화면에서 몹을 **찾는** 것이 목표다(사각형 + 이름). 무슨 몹인지만 맞히는 분류가 아니다 -
자동화에 쓰려면 좌표가 있어야 클릭할 수 있다.

```
캡처 모니터  ──담기──▶  데이터셋  ──찍기──▶  라벨  ──학습──▶  detector.zip ──▶ 찾아보기
 (WgcCaptureSession)      images/           labels/     (TorchSharp)              (DetectorModel)
```

**네 토막이 다 이어져 있다.** 앱에서 한 바퀴 밟아 확인했다(아래 "끝까지 밟아 본 것").
다만 검출 모델이 ONNX 로 안 나와 `Inference/OnnxDmlEngine` · `FramePreprocessor` 는
여전히 부르는 곳이 없다 - 남의 `.onnx` 를 돌릴 일이 생기면 그때 쓴다.

### 라벨 형식

널리 쓰는 **YOLO 형식 그대로**다. 우리끼리 쓸 형식을 새로 만들면 남이 만든 학습 코드에
넣을 때마다 옮겨 적어야 하고, 라벨을 눈으로 볼 도구도 없어진다.

```
<데이터셋>/
  images/      그림 (.png · .jpg)
  labels/      라벨 (.txt) - 그림과 이름이 같다
  classes.txt  몹 이름, 한 줄에 하나
```

라벨 한 줄은 `<몹 번호> <가운데 x> <가운데 y> <너비> <높이>` 이고 좌표는 모두 **0~1** 이다.

- **0~1 로 담는다.** 픽셀로 담으면 그림 크기가 달라지는 순간 어긋나고, 학습할 때 어차피
  다시 나눠야 한다.
- **소수점을 `InvariantCulture` 로 못 박는다.** 지역이 유럽인 PC 에서 `0,5` 로 찍히면
  빈칸으로 나눈 값의 **개수부터** 달라진다. 읽는 쪽도 같다. 검증은 실제로
  `CurrentCulture` 를 `de-DE` 로 바꿔 놓고 본다 - 코드를 읽어서는 못 잡는다.
- **사각형을 만들 때 파일과 같은 자릿수(6)로 반올림한다**(`LabelBox.Digits`).
  안 그러면 `(0.2+0.6)/2` 가 `0.4000000000000001` 이라 저장하고 되읽은 사각형이 원래 것과
  `==` 로 다르다. 눈에 안 보이는 차이지만 "고른 사각형" 을 못 찾는 식으로 샌다.
- **라벨을 다 지우면 파일도 지운다.** 빈 파일을 남기면 학습 쪽 관습으로는 "배경 사진" 이라
  사람이 의도하지 않은 것을 가르치게 된다.
- **몹은 지우지 않는다**(`LabelClasses` 에 지우기가 없다). 라벨에는 이름이 아니라 번호가
  들어 있어, 중간을 지우면 뒤가 당겨져 찍어 둔 것이 조용히 다른 몹을 가리킨다.
  이름 바꾸기는 번호가 그대로라 안전하다.

### 학습 (TorchSharp) - 실측해 둔 것

학습은 **C# 으로 한다**(`Microsoft.ML.TorchSharp` 0.23.0 의 `ObjectDetectionTrainer`,
AutoFormerV2). 스크래치 프로젝트에서 학습 → 저장 → 불러오기 → 추론까지 실제로 돌려 보고
정한 것이라, 아래 숫자는 추측이 아니라 잰 값이다.

- **.NET 10 x64 에서 돈다.** `Microsoft.ML.TorchSharp` 은 `netstandard2.0` 이지만 물린다.
  `ObjectDetection(labelColumnName, boundingBoxColumnName, imageColumnName, maxEpoch)` 로 부른다
  (`using Microsoft.ML.TorchSharp;` 가 있어야 확장 메서드가 보인다).
- 데이터 모양: `MapValueToKey` 로 라벨을 키로, `LoadImages` 로 경로를 픽셀로 바꿔 넘긴다.
  사각형은 **픽셀 좌표 (x1, y1, x2, y2)** 다 - 우리가 파일에 담는 0~1 과 다르므로 넘기기 전에
  그림 크기를 곱해야 한다.
- 모델은 ML.NET `model.zip` 으로 저장된다(약 69MB).

**ONNX 로는 못 내보낸다.** `ObjectDetectionTransformer` 가 `ICanSaveOnnx` 를 구현하지 않고
(ML.NET 의 TorchSharp 학습기는 하나도 구현하지 않는다), TorchSharp 어셈블리 안에 onnx 관련
타입이 **하나도 없다**(`torch.onnx.export` 는 파이썬 전용). 그래서 **`Inference/OnnxDmlEngine`
은 이 길에서 쓰지 않는다** - 추론도 TorchSharp 로 한다. 남의 `.onnx` 를 돌릴 일이 생기면
그때 다시 꺼내면 된다.

**CPU 로는 학습을 못 한다.** 같은 코드·같은 데이터(그림 8장, 1 epoch)로 쟀다.

| | 걸린 시간 |
|---|---|
| CPU | **248.2초** |
| CUDA (GTX 1060 3GB) | **8.6초** |

29배다. 300장 × 20 epoch 이면 CPU 로는 수백 시간이라 아예 못 쓴다. **학습은 GPU 를 전제한다.**

### 폴더 하나로 옮긴다 - %AppData%\Minguk.Tools

학습에 얽힌 것은 전부 이 뿌리 아래 둔다. 다른 PC 로 갈 때 이 폴더만 복사하면 된다.

```
%AppData%\Minguk.Tools\
├─ Datasets\몹\      images · labels · classes.txt · detector.zip · detector.json
├─ libtorch\2.2.1\   받아 둔 libtorch (CUDA 4.2GB / CPU 186MB)
├─ mlnet\            autoformer_11m_torchsharp.bin - 사전학습 가중치 보관본 (77MB)
└─ AppSettings.User.config
```

사전학습 가중치는 ML.NET 이 `%TEMP%\mlnet\` 에서만 읽고 그 자리를 바꿀 수 없다(환경 변수
없음 - DLL 문자열을 뒤져 확인). 그래서 `PretrainedWeights` 가 학습 뒤에 보관본을 챙기고,
학습 전에 임시 폴더에 없으면 보관본으로 채운다. 임시 폴더는 청소 대상이라 원본으로 믿지 않는다.

### GPU 두 장 - 한 프로세스에 한 장씩 나눈다

ML.NET 검출 학습기는 장치를 고르는 API 가 없고 늘 첫 번째 CUDA 카드를 쓴다. 한 학습을 두 장에
나누는 것(DataParallel)은 안 열려 있다. 그래서 **프로세스마다 보이는 카드를 좁힌다**
(`LibTorchRuntime.SelectGpu` → `CUDA_VISIBLE_DEVICES`, libtorch 를 올리기 전에만 먹는다).

- 하네스: `--train-check=… --gpu=0` 과 `--gpu=1` 을 창 둘에서 동시에. 3GB 카드 둘이라 각자
  320·640 처럼 다른 실험을 맡기면 시간이 반으로 준다.
- 앱: 라벨링 화면 "GPU" 선택(`Vision.GpuIndex`, -1 자동). `App.OnStartup` 이 가장 먼저 읽어
  적용하므로 **바꾸면 다시 실행해야** 한다.
- **자동은 모니터가 안 붙은 카드를 고른다**(`GpuProbe`, NVML). 게임과 바탕화면은 모니터가
  붙은 카드에서 돌기 때문이다. 다 붙었거나 다 안 붙었으면 지금 사용률이 낮은 쪽. 한 장이면
  그것. 하네스도 `--gpu=` 를 안 주면 같은 규칙이다. **고르는 순간은 libtorch 를 처음 올릴 때**
  (첫 학습·몹 찾기)다 - 앱 시작 때 고르면 그때 게임이 안 떠 있어 사용률로 못 가른다.
  이 PC 실측: 두 카드 다 모니터가 붙어 있어 사용률로 갈랐고, 게임이 도는 GPU 0(66%) 대신
  GPU 1(5%) 을 골랐다.
- 번호는 **PCI 버스 순서**로 고정한다(`CUDA_DEVICE_ORDER=PCI_BUS_ID`). 안 그러면 CUDA 가
  "빠른 카드부터" 세어 nvidia-smi 와 어긋날 수 있다. 같은 카드 둘이면 이름으로 못 가른다.
- **환경 변수는 C 런타임 표에도 써야 한다**(`_putenv_s`). `Environment.SetEnvironmentVariable`
  만으로는 libtorch 의 `getenv` 가 못 본다 - 실제로 `--gpu=1` 을 주고도 0번이 돌았다.
- 올린 뒤 로그에 "CUDA 장치 N개 보임: 0 = 이름 [PCI]" 를 적는다. Windows(WDDM)는 nvidia-smi 가
  프로세스별 카드를 안 보여 줘, 정말 그 카드로 갔는지는 이 PCI 주소로 확인한다.

**그림 캐시는 해 봤고 효과가 없었다.** 학습 전에 97장을 640x360 PNG 로 줄여 두고 학습기에
그 경로를 넘겨 2바퀴를 쟀더니 캐시 없이 411초, 캐시로 440초였다(2026-09-11). 매 바퀴
1080p 를 읽는 것이 병목일 거라는 진단이 틀렸다 - 코드는 되돌렸다(`git revert`). 학습 시간은
모델 입력 크기에 비례하고(320 은 0.6초/장, 640 은 1.5초/장) 원본 크기와 무관하다. 병목은
학습기 안의 CPU 쪽 일이다. 다시 이 길로 가지 않는다.

**CPU 100% 는 libtorch 스레드 탓이다.** GPU 로 학습해도 CPU 조각(그림 읽기·텐서 변환)이 스텝마다
끼고, 그때마다 논리 코어 수(16)만큼의 스레드가 깨어나 바쁘게 돈다. `Load` 뒤에
`torch.set_num_threads(코어/4, 2~8)` 로 제한한다. **줄이는 쪽이 오히려 빠르다** - 97장 640 1바퀴
(부대 시간 포함)가 4개 260초 · 8개 315초 · 16개 353초, CPU 는 4개 23~55% · 8개 79~100% 였다
(게임과 같이 돌 때). 스레드가 많을수록 서로 다투고 게임과도 겹친다. 실험은 환경 변수
`MINGUK_TORCH_THREADS` 로 한다.

**OpenMP 스핀도 끈다**(`KMP_BLOCKTIME=0`, `OMP_WAIT_POLICY=PASSIVE`, libtorch 올리기 전). 스레드를
4개로 줄여도 몹 찾기(640, 0.7초에 한 번)가 앱 프로세스만으로 CPU 28~30%(16코어 기준, 4.5코어)를
썼다. 일이 끝난 스레드가 다음 일을 기다리며 코어를 붙들고 있어서다. 끄자 19~20% 로 내려갔고
찾는 시간은 그대로(670~770ms)였다. 게임만 돌 때 바닥은 20~30%.

### 학습률 - 기본값 1.0 으로는 실제 화면을 못 배운다

ML.NET `ObjectDetectionTrainer` 의 기본 `InitLearningRate` 는 1.0(SGD) 이다. 확인용 단색 네모
(24장)는 그 값으로 두 바퀴 만에 loss 88 → 0.35 로 떨어져 24/24 를 찾는다. 그래서 파이프라인이
멀쩡하다고 믿고 있었는데, **실제 게임 화면 14장·27개는 320x180 20·100 바퀴, 640x360 40 바퀴
전부 0개**였다. loss 를 흘려 보니 278 로 시작해 1.4~2.1 에서 20바퀴 내내 제자리 - 배우지를
못하는 것이었다. 크기(320→640)도, 1080p 원본도, 몹 1종도 원인이 아니었다(각각 합성으로 갈라
확인). **0.1 로 낮추자 같은 데이터 20바퀴에 18/27(67%)** 을 찾았다.

- `DetectorTrainer.DefaultLearningRate = 0.1`. 하네스는 `--train-check=<폴더> --lr=` 로 바꿔 돌린다.
- **loss 는 `LogEveryNStep = 1` 로 켜야 나온다.** 기본 50 이라 14장짜리에선 거의 안 찍힌다.
  "Row: n, Loss: x" 와 "Starting/Finished epoch n" 을 progress 로 흘린다(2초에 한 줄).
- 새 데이터로 학습해서 안 찾으면 **먼저 loss 가 내려가는지** 본다. 안 내려가면 데이터를 더
  모아도 소용없다.
- **지금 보는 그림**은 "Row: n" 의 n 으로 되짚는다(`samples[n-1]`). 학습기가 순서를 안 섞는
  것을 실측으로 확인했다(1, 5, 9 … 로 늘고 바퀴마다 1로). 목록에 "◀ 학습 중" 이 따라다니고
  진행 줄에 이름이 뜬다. **선택은 기본으로 안 옮긴다** - 사람이 학습 중에 다른 그림을 찍고
  있을 수 있다. `따라가기` 를 켜면 가운데 그림도 따라간다(한 장 30~50ms, 초당 두세 장).
  목록에는 그림마다 **마지막 loss** 를 적는다 - 혼자 높은 그림이 라벨이 틀렸거나 다시 찍을
  장면이다. "이건 다시 찍어야겠다" 는 그림을 지켜보는 것보다 이 숫자로 고르는 것이 빠르다.
- **쪽지(detector.json)에 사람이 물을 만한 것을 다 적는다** - 몇 번째 학습인지(지난 쪽지 +1),
  언제, 크기, 장수·사각형·몹 종, 바퀴, 걸린 시간, GPU 여부, 학습률, 마지막 loss, 되찾기 결과.
  화면의 "모델" 줄(`ModelSummary`)이 이것을 한 줄로 보인다. 옛 쪽지는 있는 것만 적는다.
- **학습이 끝나면 학습 그림을 되찾아 목록에 "2/2" 로 적는다** (`RunSelfCheckAsync`). loss 를
  인식률로 읽는 일이 있어("인식률 맞지?") 개수로 따로 보인다. 계산은 하네스 `--detect-check`
  와 같은 `DetectionMatch`(IoU 0.5, 라벨 하나에 검출 하나) 다 - 두 벌이면 숫자가 어긋난다.
  학습에 쓴 그림이라 외운 것도 맞은 것으로 센다. 한 장 0.3초라 학습의 일부로 돈다.

### libtorch 는 받아서 쓴다

NuGet 의 `TorchSharp-cuda-windows` 를 참조하면 **빌드 출력이 3.6GB** 가 된다
(`torch_cuda.dll` 863MB · `cudnn_cnn_infer64_8.dll` 578MB · `cublasLt64_12.dll` 514MB …).
CPU 판만 해도 274MB 다. 그래서 **참조하지 않고 학습을 누를 때 받는다** - 파이썬을 처음
고를 때 받는 것(`PythonRuntimeInstaller`)과 같은 방식이다. 라벨링까지만 쓰는 사람은 안 받는다.

- csproj 에는 **관리 어셈블리인 `TorchSharp` 만** 넣는다. 여기에 딸려오는
  `LibTorchSharp.dll`(1.9MB)이 우리 쪽 껍데기라 반드시 같이 나가야 한다.
  `TorchSharp-cpu` · `TorchSharp-cuda-windows` 는 **넣지 않는다.**
- 받는 곳은 pytorch.org 의 통짜 zip 이다. NuGet 판은 크기 제한 때문에 part1~part9 로
  쪼개져 있어 런타임에 다시 붙이는 것이 번거롭다.

| | 주소 | 크기 |
|---|---|---|
| CUDA 12.1 | `download.pytorch.org/libtorch/cu121/libtorch-win-shared-with-deps-2.2.1+cu121.zip` | 2294 MB |
| CPU | `download.pytorch.org/libtorch/cpu/libtorch-win-shared-with-deps-2.2.1+cpu.zip` | 177 MB |

판(2.2.1)은 TorchSharp 0.102.7 이 요구하는 것과 맞춰야 한다. 어긋나면 진입점을 못 찾는다.

- 물리는 법은 **`NativeLibrary.Load` 로 전체 경로를 주어 `torch_cpu.dll` · `torch.dll` 을
  먼저 올리는 것**뿐이다. 그 뒤 `torch.ones(...)` 가 그냥 된다.
  `AddDllDirectory` 는 안 써도 된다(실제로 87 로 실패하는데도 로드는 됐다).
- 받은 폴더를 안 주면 TorchSharp 가 제 진단문("Giving up, TorchSharp.dll does not appear to
  have been loaded from package directories")과 함께 실패한다 - **NuGet 캐시를 주워 쓰지
  않는다는 것을 이걸로 확인했다.**
- 받는 자리는 실행 폴더가 아니라 `%AppData%` 밑이어야 한다. Velopack 이 업데이트 때
  설치 폴더를 통째로 갈아 끼우므로 2GB 를 매번 다시 받게 된다(`Helper/UserDataPaths` 참고).

### 추론

학습해 둔 `detector.zip` 을 그대로 돌린다(`Vision/Inference/DetectorModel`).
**`Inference/OnnxDmlEngine` 은 이 길에 쓰이지 않는다** - 이 모델은 ONNX 로 못 나온다.
남이 만든 `.onnx` 를 돌릴 일이 생기면 그때 그쪽을 꺼내 쓰면 된다.

- 모델은 **픽셀** 좌표를 내놓는다. 우리는 0~1 로 다루므로 받자마자 나눈다.
- **찍은 사각형은 옮기고 크기를 바꿀 수 있다.** 계산은 `LabelBoxEdit`(순수, `--vision` 이
  숫자로 본다)에 있고 캔버스는 마우스만 잇는다. 고른 것의 모서리·변 가운데 손잡이는 크기
  조절, 안쪽은 옮기기(안 고른 것도 누르는 순간 고르고 끌린다), 빈 자리는 새로 그리기다.
  손잡이는 **고른 것에서만** 잡힌다 - 안 고른 것의 모서리까지 잡으면 붙어 있는 사각형 사이에
  새로 그리려다 엉뚱한 것이 늘어난다. 화살표는 1px, Shift+화살표는 10px 씩 민다.
- 옮기기는 **시작 사각형에 변위를 더한다**. 직전 위치에 더하면 반올림이 쌓인다. 4px 안쪽의
  떨림은 옮기기로 안 친다 - 고르려고 누를 때마다 사각형이 흘러 저장할 때마다 좌표가 바뀐다.
  마우스로 실제로 끌리는지는 `--canvas-drag` 가 본다(커서를 가져가므로 `--views` 밖).
- **손을 덜 타게 하는 세 가지.** 다 손으로 그리는 것은 "일일이 잡아줘야 하나" 였다.
  (1) `앞 장 가져오기` - 앞에서 가장 가까운 라벨 있는 그림의 사각형을 더한다. 연달아 담은
  그림은 몹이 몇 픽셀만 움직이니 가져와 끌어 맞춘다. (2) `점선 받기` - 모델이 찾은 점선을 전부
  라벨로 굳히고 틀린 것을 지운다. 캔버스에서 점선을 누르면 그것 하나만 옮겨지고 바로 끌린다.
  (3) 캡처 모니터에서 몹 찾기가 켜진 채 담으면 1초 안에 찾은 것을 라벨 파일로 같이 쓴다
  (`FreshDetections`). 찾은 것이 없으면 라벨 파일을 안 만든다 - 빈 라벨은 "몹이 없다" 를
  가르치는 것이라 사람이 봐야 한다.
- 예측 이름을 **번호로 되돌려** 손으로 찍은 사각형과 같은 색이 되게 한다. 색까지 다르면
  어느 몹을 찾았는지 알아보기 어렵다. 학습 파이프라인 끝에 `MapKeyToValue` 를 붙여
  모델이 번호가 아니라 이름을 내놓게 해 두었다.
- **세 배열(이름·사각형·점수)의 길이가 맞는지 본다.** 사각형은 넷씩 묶여 있어 개수가
  어긋나면 엉뚱한 이름이 엉뚱한 자리에 붙는데, 화면에는 그럴싸하게 그려져 눈으로는 못 잡는다.
- 열 이름(`PredictedLabel` · `PredictedBoundingBoxes` · `Score`)은 `DetectorTrainer` 의
  상수를 양쪽이 같이 본다. 어긋나면 조용히 빈 배열이 와서 "못 찾았다" 로 보인다.
- 모델은 한 번 읽고 들고 있는다(69MB). 파일 경로가 바뀌었으면(다시 학습) 버리고 새로 읽는다.
- `PredictionEngine` 은 스레드 안전하지 않다. 프레임마다 부르게 되면 한 스레드로 몰아야 한다.

화면에서는 **점선이 모델이 찾은 것, 실선이 사람이 찍은 것**이다. 섞어 그리면 무엇이 내가
찍은 것인지 갈리지 않는다. 예측은 고를 수도 지울 수도 없다 - 고칠 것은 사람이 찍은 쪽뿐이다.

### 확인용 데이터로 시험할 때

확인용 씨앗을 심는 스크립트는 `images/` · `labels/` 를 **비우고** 시작한다. 사용자가
게임에서 모은 그림이 그 자리에 있으면 통째로 날린다. **사용자가 모으기 시작한 뒤에는
기본 데이터셋 폴더에 씨앗을 심지 않는다** - 시험은 다른 폴더를 만들어 `폴더 고르기` 로
돌려 놓고 한다.

### 끝까지 밟아 본 것

2026-09-10 에 앱에서 한 바퀴 돌려 확인했다. 씨앗 그림 24장(빨간 네모=슬라임,
초록 네모=버섯) · 사각형 48개 · 20 epoch.

```
학습 3.9분 (GPU, GTX 1060 3GB)  ->  detector.zip 68.9MB
찾아보기                        ->  2마리 찾았습니다: 슬라임 100%, 버섯 99%
```

점선이 손으로 찍은 실선과 거의 정확히 겹쳤다. **이 숫자가 기준선이다** - 나중에 같은 데이터로
훨씬 오래 걸리거나 못 찾으면 무언가 어긋난 것이다.

### 모델이 보는 크기 - 파이프라인 안에서 맞춘다

**AutoFormerV2 는 넣은 그림을 줄이지 않고 그대로 본다.** 그래서 크기를 안 맞추면 두 가지가
한꺼번에 어긋난다.

- 값이 픽셀 수에 비례한다. 1920x1080 한 장이 **4.7초**였다(320x200 은 220ms).
  그 크기로 학습하면 몇십 배가 걸린다.
- 담기는 원본 해상도로 저장하고 실시간 쪽은 320 으로 줄여 넣으므로, **학습한 크기와
  추론하는 크기가 다르다.** 실측으로 그 어긋남은 검출을 망친다.

그래서 `ResizeImages` 를 **모델 파이프라인 안에** 넣었다(`DetectorTrainer.InputWidth` ·
`InputHeight` = 320x180). 저장된 모델이 그것을 들고 다니므로 추론 쪽은 아무것도 안 해도
학습 때와 같은 것을 본다. 고친 뒤 실측:

| 넣은 크기 | 고치기 전 | 고친 뒤 | 찾은 것(2개 중) |
|---|---|---|---|
| 160x90 | 121 ms | 240 ms | 1 → **2** |
| 320x180 | 220 ms | 232 ms | 2 |
| 480x270 | 439 ms | 230 ms | 2 |
| 640x360 | 587 ms | 229 ms | 2 |
| 960x540 | 1,064 ms | 230 ms | 2 |
| **1920x1080** | **4,724 ms** | **236 ms** | 1 → **2** |

넣는 크기와 상관없이 **~230ms (4.2~4.4 fps)** 로 같아지고, 모든 크기에서 둘 다 찾는다.
`dotnet run --project Minguk.Tools.Tests -- --detect-bench` 로 잰다.

- **`Fill`(늘려 채우기)로 맞춘다.** 그래야 0~1 라벨이 그대로 곱해져 맞는다 - 축마다 따로
  늘어나기 때문이다. `IsoPad` 로 여백을 두면 라벨 쪽에서도 같은 여백을 계산해 줘야 해서
  어긋나기 쉽다. 게임 화면이 대개 16:9 라 320x180 이면 찌그러짐도 거의 없다.
- **사각형 픽셀 좌표는 원본이 아니라 이 크기로 만든다**(`TrainingSample.From`).
  원본을 곱하면 사각형만 원본 자리에 남고 그림은 줄어들어 통째로 어긋나는데, 학습은 그대로
  돌아가고 몇 시간 뒤에 아무것도 못 배운 모델이 나온다.
- 추론이 돌려주는 좌표도 이 크기의 것이다. **파일 크기로 나누면 틀린다.**
- **담아 두는 그림은 원본 크기 그대로 둔다.** 나중에 다른 크기로 다시 학습할 수 있고,
  라벨은 0~1 이라 크기를 안 탄다.

### 크기를 바꾸려면

화면(라벨링 → 학습 칸)에서 **320x180 · 480x270 · 640x360 · 960x540** 중에 고른다.
1080p 화면에서 60px 짜리 몹은 320x180 으로 줄이면 10px 가 되어 잘 안 잡힌다 -
**작은 몹을 놓칠 때만** 키운다. 대신 위 표만큼 느려지고 학습 시간도 같은 비율로 늘어난다.

**바꾸면 반드시 다시 학습해야 한다.** 그리고 그 크기는 **설정이 아니라 모델과 함께** 남긴다
(`detector.json`, `DetectorManifest`).

설정에서 읽으면 320 으로 학습해 둔 모델을 640 설정으로 읽는 순간 **좌표가 조용히 어긋난다** -
추론이 돌려주는 픽셀을 640 으로 나누게 되어 사각형이 절반 자리에 그려진다. 화면에는
그럴싸한 사각형이 떠서 눈으로는 못 잡고, 사람은 모델이 못 배웠다고 여기고 데이터를 더
모으러 간다. 쪽지로 남기면 설정을 아무리 바꿔도 이미 만들어진 모델은 제 크기로 정확히 돈다.

쪽지가 없거나 망가졌으면 옛 값(320x180)으로 본다. 쪽지 하나 때문에 학습해 둔 모델을
못 쓰게 되는 것이 더 나쁘다. 쪽지에는 학습한 시각·장수·바퀴 수·몹 이름도 같이 적어,
데이터셋을 고친 뒤 다시 학습했는지 눈으로 가늠할 수 있게 해 둔다.

**640x360 으로 실제로 학습해 본 것** (같은 씨앗 24장 · 20 epoch, GTX 1060):

| | 320x180 | 640x360 |
|---|---|---|
| 학습 | 4.2분 | **10.5분** |
| 추론 한 장 | ~230 ms | **~650~830 ms** |
| fps | 4.3 | **1.2~1.5** |

픽셀 4배에 학습 2.5배 · 추론 3배. 넣는 파일이 160x90 이든 1080p 든 값이 같았고 둘 다
찾았다 - 추론이 파일 크기가 아니라 **쪽지의 640x360** 으로 좌표를 되돌린 것이다.
**640 으로 올리면 0.25초에 한 번은 못 돌리고 0.8초에 한 번쯤 된다.** 작은 몹 때문에
올릴 때 그것을 감수하는 것이다.
- 파일을 읽는 값은 여전히 무시할 만하다(1080p 에서 6.4ms / 236ms).
- GPU 는 쓰고 있다. 추론 중 `nvidia-smi` 로 1,424 MiB 를 물고 있었다.

**캡처 프레임마다 돌리는 실시간 겹쳐 그리기는 여전히 안 된다.** 30fps 면 한 장에 33ms 인데
230ms 다. 대신 0.25초에 한 번은 넉넉하다 - 아래 「캡처 화면에서 찾기」가 그 갈래다.

### 스크립트 화면에서 찾기

스크립트 화면(플레이 화면도 같다) 도구 줄의 **몹 찾기**를 켜면 프레임에서 몹을 찾아 미리보기 위에 점선으로
겹쳐 그린다. 옆에 몇 마리를 몇 ms 에 찾았는지 같이 뜬다.

- **프레임마다 안 돌린다.** 0.25초에 한 번이고(`DetectIntervalMs`), 앞의 것이 아직 돌고
  있으면 그 프레임은 그냥 흘린다. 큐에 쌓으면 화면이 점점 뒤처진 결과를 보여 준다 -
  실시간에서는 늦은 답이 틀린 답이다.
- **캡처 스레드에서 기다리면 안 된다.** 220ms 를 잡고 있으면 프레임이 통째로 밀린다.
  거기서는 줄여서 파일로 떨어뜨리기만 하고(픽셀은 콜백이 돌아가면 사라진다) 추론은
  백그라운드로 보낸다.
- 긴 변 **320** 으로 줄여 저장한다(`DetectLongestSide`). 크기를 맞추는 것은 모델이 하므로
  정확도와는 무관하고, PNG 로 만드는 값을 아끼는 것뿐이다.
- 켤 때 CPU 리드백이 꺼져 있으면 **알아서 켜고 그렇게 적는다.** 픽셀이 CPU 로 안 내려오면
  넣을 것이 없는데, 그냥 "안 된다" 고만 하면 왜인지 알 수 없다.
- 모델을 못 읽거나 추론이 터지면 **토글을 끈다.** 안 그러면 매 프레임 같은 오류를 쏟는다.
- 겹쳐 그리는 것은 `Markup/DetectionOverlay` 다. `LabelCanvas` 와 달리 그림은 안 그린다 -
  미리보기는 D3DImage 를 `Image` 요소가 60fps 로 그리는 빠른 길이라 주체를 바꾸지 않는다.
  `Source` 로 원본 크기만 받아 `Stretch=Uniform` 레터박스를 스스로 계산하고,
  마우스는 통과시킨다(미리보기는 클릭을 대상 창으로 넘기는 일을 하고 있다).
- **프레임 간 추적**(`DetectionTracker`, 토글 `추적`, 기본 켬). 검출기는 프레임을 따로 봐서
  30~50% 헛것이 한 프레임 튀고 몹이 반 프레임 가려지면 깜빡인다. 지난 사각형과 IoU 0.3 으로
  이어, 두 번 연속 보인 것만 내놓고 두 번까지는 놓쳐도 잇는다(0.25초 주기라 각 0.5초).
  자리는 새 값 0.6 비중으로 부드럽게. 겹침이 0 이어도 가운데가 사각형 크기의 1.5배 안이면
  같은 몹으로 잇는다 - 640 모델(0.65초/장)은 시선을 돌리면 몹이 제 폭보다 더 옮겨 가,
  겹침만 보면 옛 자리에 유령 사각형이 남았다(실측: 봇마다 오른쪽에 86% 짜리 하나씩).
  누르기·자동 라벨도 추적 결과를 쓴다 - 보이는 것과 누르는 것이 달라선 안 된다. 순수 클래스라 `--vision` 이 시나리오로 본다. 모델을 새로
  읽거나 끄면 잊는다.
- **다시 학습하면 새 모델을 알아서 읽는다.** 모델 파일의 쓴 시각을 들고 있다가, 켤 때와
  켜 둔 채 5초마다(캡처 스레드, `MaybeReloadDetector`) 비교해 다르면 옛것을 버리고 새로
  읽는다(`LoadDetector`). 이게 없을 때는 화면을 닫았다 열어야 했다. **쓰는 도중에 읽지
  않는다** - 69MB 를 쓰는 동안 시각이 계속 바뀌므로 같은 시각이 2초 유지된 뒤에 읽는다.
  돌고 있는 추론이 옛 모델을 쓰는 중일 수 있어 필드를 먼저 비우고 끝나기를 기다렸다 놓는다.
- **실시간 경로와 파일 경로는 같은 답을 낸다**(`--scale-check` 실측: 원본 62/64 · 평균 81%,
  WPF 로 640 으로 줄인 PNG 60/64 · 80%. 차이는 가장 작은 봇 둘뿐). 실시간에서 흔들리면
  경로가 아니라 **거리**(학습 범위 밖의 작은 봇)와 **움직임**(0.65초 사이 이동)을 본다.
- **추론용 임시 PNG 는 모델이 보는 크기 이상으로 만든다**(`Math.Max(320, InputWidth)`). 320 으로
  고정했더니 640x360 모델이 320 짜리를 도로 키워 봐서, 되찾기 검사(원본)는 97% 인데 실시간은
  작은 봇을 못 봤다. 상태 줄의 "(320x180, …ms)" 가 그 임시 그림 크기다.
- 추론용 임시 PNG 는 프로세스마다 이름을 나눈다. 앱이 강제로 죽으면 정리가 안 돌아
  실제로 두 개가 남았던 적이 있어, 켤 때 옛것을 치운다.
- **프레임 저장·담기·몹 찾기는 CPU 리드백을 알아서 켠다**(`EnsureCpuReadback`). 리드백은 세션을 만들 때
  정해져서 도는 중에 값만 바꾸면 헛것이다 - **껐다 켜는 것이 유일한 길**이고, 통계 몇 초
  말고는 잃는 것이 없다. 버튼을 회색으로 두고 이유를 안 알려 주면 "몹을 모을 수가 없다"
  가 된다. 실제로 그랬다.
- **담긴 결과는 아래 바에 "N장째" 로 남긴다.** 통계 표의 비고 칸에만 적으면 사실상 안 보이고,
  상태 줄은 1초마다 통계 갱신이 덮어쓴다. 아래 바(`SendMainMessage`)가 남는 자리다.
  프레임 저장(`captures/`)도 같은 모양으로 알린다 - 한쪽만 조용하면 고장으로 보인다.
- **담기는 F8 전역 단축키로도 된다.** 몹은 게임 안에서 보이는데 버튼은 이 앱이 앞에 있어야
  눌린다. 매번 넘어왔다 돌아가면 그 사이 몹이 움직인다("왔다갔다 힘드네"). 입력 자동화가
  F3~F6 을 쥐므로 겹치지 않는 키다 - 두 화면이 같이 열리면 겹친 쪽은 등록이 실패한다.
  버튼과 단축키는 `RequestCollectFrame` 한 길로 온다.
- **단축키로 담을 때는 소리로 답한다.** 누르는 순간 사용자는 게임을 보고 있어 상태 줄이
  안 보인다. 담기면 알림음, 캡처가 안 돌거나 실패하면 거절음. 소리가 없으면 담겼는지 몰라
  몇 번씩 누르고, 같은 장면이 여러 장 쌓인다. 버튼으로 담을 때는 글이 보이니 소리를 안 낸다.
- **못 켜는 이유는 아래 상태 줄(`StatusText`)에 적는다.** 도구 줄의 정적 항목은 짧은
  "2마리 (…ms)" 는 그리는데 긴 한글 안내는 값이 들어 있어도 안 그렸다(로그로 확인). 타이밍·
  캡처 중 여부·`AutoSizeMode=Fill` 까지 갈라 봤지만 이유를 못 밝혔다. 그래서
  `TurnOffDetection` 이 상태 줄에도 같이 적는다.
- **끄면서 이유를 적을 때는 먼저 끄고 나서 적는다.** 토글을 끄면 콜백이 다시 돌아
  안내를 지운다. 실제로 그래서 "몹 찾기를 눌러도 아무 일도 없는" 것처럼 보였다.
- **UIA 로 확인할 때 `dxe:TextEdit` 는 Text 가 아니라 Edit 다.** 값은 `ValuePattern` 에 있다.
  Text 만 훑고 "안 뜬다" 고 결론 내렸다가 한참 돌았다. 화면을 의심하기 전에 값이 들어
  있는지 로그로 먼저 본다.

**찾은 것 누르기** - 가장 자신 있는 몹의 가운데를 누른다. 찾기만 하면 자동화가 아니다.

- **입력 전달이 켜져 있어야 나간다.** 찾는 것은 화면만 보는 일이라 대상에 아무 영향이
  없지만, 누르는 것은 남의 프로그램에 실제로 들어간다. 그것을 켜는 일은 사람이 한 번 분명히
  해야 한다 - 몹 찾기를 켠 것만으로 클릭이 나가면 안 된다. 실측: 꺼진 채로 누르면 0건,
  켜고 누르면 1건이 찾은 사각형 안쪽에 떨어졌다.
- 누르는 길은 미리보기를 손으로 누를 때와 **같은 것**(`SendClickAsync`)이고, 좌표 계산도
  `PreviewInputMapper.MapRatioToScreen` 하나를 나눠 쓴다. 두 벌로 두면 한쪽만 고쳐져
  손으로는 되는데 자동으로는 안 되는 일이 생긴다.
- 검출은 0~1 이라 미리보기 컨트롤을 안 거친다. 미리보기가 꺼져 있어도 눌린다.
- 되풀이해서 누르는 것은 아직 없다. 그것은 입력 자동화의 반복·중지·단축키가 이미
  있으므로 그쪽(스크립트)에서 검출 결과를 받아 쓰는 갈래로 붙이는 것이 맞다.

**앱에서 밟아 확인한 것**: 학습한 색 네모를 띄운 창을 잡아 켰더니 도구 줄에
`2마리 (320x208, 257ms): 버섯 100%, 슬라임 100%` 가 뜨고 점선이 네모에 맞았다.
그동안 캡처는 10fps 로 계속 돌았고 **지연 평균 0.00ms** 였다 - 추론이 캡처를 안 막는다.

### 글자 읽기 (OCR)

`Windows.Media.Ocr` 을 쓴다(`Vision/Ocr`, 인터페이스 `IOcrEngine` · 구현 `WindowsOcrEngine` · 팩터리
`OcrEngineFactory`). 따로 받을 것이 없고 한글을 읽으며 단어마다 자리가 온다. 이 PC 에는 한국어
팩(ko)만 있는데 그것으로 영문·숫자도 읽는다. 언어 팩이 없으면 팩터리가 "설정 > 시간 및 언어에서
추가" 라고 말한다.

- **영역을 정해서 읽는다.** 화면 전체는 느리고 엉뚱한 글이 섞인다. 캡처 모니터 `글자 영역` 을 켜고
  미리보기에서 끌면 0~1 비율로 저장된다(`OcrRegion` 설정). 끄는 동안은 클릭이 게임으로 안 나간다.
- 0.5초에 한 번, 검출과 같은 단일 실행 규칙. 프레임에서 그 영역만 복사한다(전체 복사 안 함).
- **작은 글자는 키워서 넣는다**(높이 160 아래면 최대 4배). 게임 UI 글자(12~20px)는 안 키우면 놓친다.
  자리는 비율이라 키운 것과 무관하다.
- `--vision` 이 WPF 로 그린 "HP 1234" · 14px "LV 57" · 빈 그림을 읽어 확인한다.
- **몹 이름표**(`이름표 읽기`)는 고정 영역이 아니라 검출 사각형마다 위쪽을 잘라 읽는다
  (`NameplateRegion.Above`: 높이 45%, 너비 1.8배, 5% 겹침). 검출은 줄인 그림으로 하지만 글자는
  12px 이라 원본이 필요해서, 검출 주기마다 프레임을 한 벌 복사해 둔다(`CopyFrameForNameplates`,
  8MB/0.65초). 읽은 이름은 캡션에 「」 로 붙는다. 한 장 10~20ms 라 검출 뒤에 이어도 표가 안 난다.
- **이름표는 그대로 넣으면 안 읽힌다.** 13px 빨간 글자가 회색 벽 위라 두 언어 모두 빈 글이었고, 6배로
  키워도 마찬가지였다. `NameplateInk` 가 빨간 픽셀(R>150, G·B 보다 60 이상)만 검정으로 남기고 3배로
  키운다. 실측(`--nameplate-check`): 8곳 중 6곳 「일반봇」, 2곳 「일반못」. 6배는 체력 바의 빨간 칸까지
  글자로 착각해 쓰레기 줄이 붙어 3배로 정했다. 실시간에서도 「일반봇」이 붙는 것을 확인했다. 아군
  파랑·다른 색 이름표는 `NameplateInk.IsInk` 만 바꾼다. 이름표는 **한국어 엔진**이어야 한다 -
  en-US 는 한글을 못 읽어 `OIHY` 같은 것을 낸다.
- `--nameplate-check [--root=] [--count=3] [--out=]` 가 데이터셋 라벨 위를 잘라 언어별로 원본·전처리
  결과를 나란히 찍고 조각 그림을 `%TEMP%\minguk-nameplate` 에 남긴다. 전처리를 손보면 여기서 비교한다.

### 화면

- 데이터셋 자리는 **캡처 화면과 라벨링이 같이 본다**(`LabelDataset.ConfiguredRoot`,
  앱 전체 키 `Vision.DatasetRoot`). 화면마다 설정을 들면 한쪽에서만 폴더를 바꿔 놓고
  담은 그림이 왜 안 보이는지 한참 찾게 된다.
- **그림을 넘길 때 자동으로 저장한다.** 수백 장을 찍는 일이라 장마다 저장을 누르게 하면
  반드시 잊고, 잊은 것은 되돌릴 수 없다.
- `Markup/LabelCanvas` 가 그림과 사각형을 **직접 그린다**(`OnRender`). Viewbox 로 늘리면
  테두리와 글자까지 같이 늘어나 확대한 그림에서 경계가 안 보인다. 그림만 늘린다.
  화면 좌표 ↔ 0~1 변환은 이 안 두 곳(`ToScreen`·`ToNormalized`)에서만 한다.
- 캔버스는 컬렉션(`Boxes`)을 **직접 고친다**. 사각형 하나 그릴 때마다 커맨드로 올렸다
  내리면 좌표를 두 번 옮겨 적게 되고 얻는 것이 없다. ViewModel 은 `CollectionChanged` 로 안다.
- 몹 색은 번호에서 만든다(황금각 137.5도). 목록에 색을 적어 두면 몹이 늘 때마다 색을
  새로 골라야 한다.
- 학습은 **라벨링 화면 아래 칸**에 있다. 바퀴 수를 정하고 누르면, libtorch 가 없으면
  먼저 받고(물어본다) 이어서 학습한다. "받기" 버튼을 따로 두지 않는 것은, 그러면 사람이
  그것을 먼저 눌러야 한다는 걸 알아야 하고 안 눌렀을 때 학습이 왜 안 되는지도 설명해야 해서다.
- **그리드 검색 창은 XAML 의 `ShowSearchPanelMode="Never"` 만으로 안 꺼진다.** 저장된 그리드 배치
  (`LayoutSerializationService`)에 `ActualShowSearchPanel=true` 가 들어 있어 복원이 다시 펼친다.
  복원 뒤 `ShowSearchPanelMode=Never` 를 다시 놓고 `HideSearchPanel()` 을 불러도, `DXSerializer.AllowProperty`
  로 막아도 안 접혔다. 캡처 모니터는 복원 전에 그 항목을 배치 글에서 지운다(`RestoreGridLayout`).
  `BaseTableView` 기본은 Always 라 다른 화면은 그대로 검색 창이 있다.
- **서비스를 View 에 선언하지 않으면 `Guard` 가 예외를 삼켜 아무 일도 안 일어난 것처럼 보인다.**
  실제로 그랬다 - `DXMessageBoxService` 를 빼먹어 학습 시작을 눌러도 화면이 그대로였고,
  로그에도 안 남았다. 새 서비스를 쓸 때는 View 의 `Interaction.Behaviors` 부터 본다.
- **목록의 줄은 `LabelingRow` 로 감싼다.** `LabelItem` 은 값이고 `HasLabel` 은 그때그때
  `File.Exists` 를 보므로, 그대로 얹으면 화면이 한 번 읽고 그만이라 **방금 저장했는데도
  표시가 안 켜진다**(실제로 그랬다). 컬렉션의 줄을 갈아 끼워 다시 그리게 하는 방법은
  고른 줄이 풀리면서 `SelectedItem` 이 null 로 떨어져 찍던 사각형이 지워진다.

## 검증

`Minguk.Tools.Tests` 는 단위 테스트가 아니라 **실행형 하네스**다. 실제 커서와 살아 있는 창을
확인하므로 목으로는 대신할 수 없다. 자세한 것은 `Minguk.Tools.Tests/README.md`.

```
dotnet run --project Minguk.Tools.Tests -c Debug -- --backend=SendInput   # 경로별 전체
dotnet run --project Minguk.Tools.Tests -c Debug -- --views               # 화면 생성만 (안전)
dotnet run --project Minguk.Tools.Tests -c Debug -- --vision              # 라벨·학습 준비·추론 변환 (안전, 2초)
dotnet run --project Minguk.Tools.Tests -c Debug -- --canvas-drag         # 라벨 캔버스를 실제 마우스로 끌기 (커서 3초)
dotnet run --project Minguk.Tools.Tests -c Debug -- --fallback            # 드라이버 없는 상황
dotnet run --project Minguk.Tools.Tests -c Debug -- --calibrate           # 정규화 규칙 실측
dotnet run --project Minguk.Tools.Tests -c Debug -- --detect-bench        # 추론 속도 (libtorch 필요)
dotnet run --project Minguk.Tools.Tests -c Debug -- --detect-check        # 학습한 모델이 라벨을 되찾는지 (libtorch 필요)
dotnet run --project Minguk.Tools.Tests -c Debug -- --scale-check         # 실시간 경로(WPF 축소 PNG)와 파일 경로가 같은 답인지
```

- `--views` · `--vision` · `--detect-bench` 외에는 **커서와 키보드를 가져간다.** 돌리는 동안 손을 떼야 한다.
- **라벨·학습·추론만 고쳤으면 `--vision` 이면 된다.** 이것들을 `--backend=` 안에 얹어 두면
  라벨 하나 고치고 확인하려 해도 상관없는 입력 어댑터 검증이 통째로 딸려 오고, 그동안
  커서를 못 쓴다. `--backend=` 로 도는 전체 검증에도 그대로 끼므로 한 번에 다 볼 수도 있다.
- `--detect-bench` 는 libtorch(4GB)와 학습한 `detector.zip` 이 있어야 돈다. 없으면 그렇게 말하고 끝난다.
- **학습이 끝나면 `--detect-check` 로 본다.** "끝났습니다" 는 모델이 만들어졌다는 뜻이지 찾는다는
  뜻이 아니다. 라벨 찍은 그림마다 검출을 돌려 IoU 0.5 이상으로 되찾은 개수와 헛것을 센다.
  문턱 기본은 앱과 같은 50% (`--score=` 로 바꾼다). 실측(14장·60바퀴): 50% 에서 17/27 · 헛것 9,
  30% 에서 23/27 · 헛것 70.
  학습에 쓴 그림이라 외운 것도 맞은 것이 된다 - 여기서 못 찾으면 확실히 문제고, 다 찾아도
  새 장면은 캡처 화면의 몹 찾기로 따로 봐야 한다.
- 화면을 고쳤으면 `--views` 를 돌린다. XAML 은 빌드를 통과하고 런타임에만 터진다.
- 입력 쪽을 고쳤으면 **세 경로 모두** 돌린다. 한 경로만 통과하는 변경이 흔하다.
- 한글 쪽을 고쳤으면 하네스만으로 부족하다. 대상이 WPF 라 최상위 창과 포커스 창이 같아서,
  Win32 대상에서만 드러나는 IME 문제를 못 잡는다. 메모장으로도 한 번 쳐 볼 것.

## 규칙

- 폰트·크기는 `BaseFontFamily` / `BaseFontSize` 리소스를 **DynamicResource** 로 참조한다. StaticResource 로 쓰면 설정 변경이 반영되지 않는다.
- 경량 테마를 쓰므로(`UseLightweightThemes = true`) 표준 WPF 컨트롤에는 테마가 적용되지 않는다. 화면은 DevExpress 컨트롤로 짠다.
- 그리드는 `Minguk.Base.Controls.BaseGridControl` / `BaseTableView` 를 쓴다. 기본값이 이미 잡혀 있다.
  - 행 번호를 쓸 때(`GridControlDependency.IsRowNumber="True"`) **화면에서 `IndicatorWidth` 를 물려야 한다.**
    `InitRowIndicatorWidth` 가 폭을 계산해 두지만 인디케이터 칸에 연결하는 쪽이 없으면 세 자리부터 앞이 잘린다.
    그리드를 `Grid.IsSharedSizeScope="True"` 로 감싸고 `SharedSizeGroup="RowNumberGroup"` 열에 숨은 `Border` 를 둔 뒤
    `IndicatorWidth="{Binding Path=(Border.ActualWidth), ElementName=..., Mode=OneWay}"` 로 되돌린다.
    (`Views/CaptureMonitorView.xaml` 이 예시다)
  - `GridControlDependency.IsColumnAutoWidth` 를 켜면 모든 열의 너비 단위가 `Auto`(내용에 맞춤)가 된다.
    그 상태에서는 열 경계를 끌어도 값이 남지 않는다. 사용자가 너비를 직접 잡게 하려면 꺼야 한다.
- `ItemsSource` 가 있는 콤보는 **`SelectedItem` 으로 묶는다.** `EditValue` 로 묶으면 고른 것이
  콤보에는 보이는데 ViewModel 은 그대로여서, **화면과 실제 동작이 어긋난다** —
  입력 자동화 화면에서 콤보는 PostMessage 라고 하는데 실제로는 SendInput 으로 나가고 있었다.
  `Mode=TwoWay, UpdateSourceTrigger=PropertyChanged` 를 함께 적는다.
- ViewModel 간 통신은 직접 참조 대신 `MessengerUtility` 를 쓴다.
- 예외는 `ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName())` 로 보여 주고 NLog 로 남긴다.
- `Libs/DirectML/x64/` 의 DirectML 네이티브는 NuGet 이 아니라 저장소에서 나간다.
  `Microsoft.AI.DirectML` 패키지는 `ExcludeAssets="all"` 로 자기 복사를 꺼 두었다 —
  안 끄면 같은 파일을 둘이 각각 복사해서, 어느 것이 출력에 남는지가 빌드 순서에 달린다.
  - 버전을 올릴 때 패키지 버전만 바꾸면 안 된다. 새 패키지의 `bin/x64-win/` 에서 DLL 을 꺼내
    `Libs/DirectML/x64/` 를 덮어쓰고 **같이 커밋한다**. `onnxruntime` 과 DirectML 은 짝이 맞아야 하고,
    한쪽만 올리면 빌드는 통과하고 세션을 만들 때 터진다.
  - 관리 어셈블리(`Microsoft.ML.OnnxRuntime.dll`)는 그대로 NuGet 이 맡는다.
    `System.Numerics.Tensors` 를 물고 있어서 손으로 따라가면 놓치기 쉽다.
  - `DirectML.Debug.dll` 은 Debug 구성에만 들어간다. 패키지의 targets 는 구성을 보지 않으므로
    (`Microsoft_AI_DirectML_SkipDebugLayerCopy` 로만 가른다) csproj 에서 직접 나눈다.
  - `.pdb` 는 저장소에 두지 않는다. 남의 네이티브 라이브러리 안으로 들어갈 일이 없다.
- **Interception 드라이버**는 `Input/Adapters/InterceptionDriver` 가 살피고 설치한다.
  - 상태를 `keyboard` · `mouse` **서비스**로 읽는다. 어댑터의 `IsAvailable` 은 "쓸 수 있는지" 만
    말하고 **왜** 못 쓰는지는 모른다 - 설치가 안 된 것과 재부팅을 안 한 것은 사용자가 할 일이 다르다.
  - 설치는 **별도 프로세스를 `runas` 로** 띄운다. 앱 전체를 관리자로 올리면 안 된다 —
    관리자 창에는 탐색기에서 파일을 끌어다 놓을 수 없고(UIPI) 늘 UAC 를 거쳐 켜야 한다.
  - 설치 뒤에는 **반드시 재부팅**이다. 키보드·마우스 장치 스택 사이에 끼어드는 필터 드라이버라
    이미 올라온 장치에는 다음 부팅에야 붙는다. 안내 줄만으로는 지나치기 쉬워 대화 상자로도 알린다.
  - `install-interception.exe` 는 `Native/x64/` 에 함께 둔다. **`interception.dll` 과 같은
    릴리스(v1.0.1)여야 한다** - 넣을 때 릴리스 안의 dll 이 우리 것과 바이트 단위로 같은지 확인했다.
    설치 프로그램 껍데기는 코드 서명이 없어 SmartScreen 경고가 뜰 수 있다(드라이버 .sys 는 서명됨).
- `interception.dll` 은 `Minguk.Tools/Native/x64/` 에 있다. 이 프로젝트만 쓰므로 `Libs/` 가 아니다.
  `DllImport` 가 실행 파일 옆에서 찾으므로 `TargetPath` 로 출력 루트에 떨어뜨린다.
  x86 빌드를 잘못 넣으면 컴파일은 통과하고 실행할 때 `BadImageFormatException` 으로 터진다.
- `Minguk.Image` 는 솔루션에 없다. `Libs/Minguk.Image.dll` 을 `Reference` + `HintPath` 로 무는, DirectML 같은 외부 바이너리 취급이다. csproj 에 `<Resource Include>` 가 4만 개라 소스로 두면 빌드마다 그 csproj 평가에 시간을 버리기 때문이다. 아이콘을 바꿔야 하면 `Minguk.Image/Minguk.Image.csproj` 를 단독으로 Release|x64 빌드해서 나온 DLL 로 `Libs/Minguk.Image.dll` 을 덮어쓰고 커밋한다.
