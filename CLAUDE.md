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
| `IInputAdapter` | `SendInputAdapter` · `PostMessageInputAdapter` · `InterceptionInputAdapter` | `InputAdapterFactory` |
| `IUiAutomationAdapter` | `WindowsUiAutomationAdapter` | `UiAutomationAdapterFactory` |
| `IGlobalHotkeyAdapter` | `GlobalHotkeyAdapter` | `GlobalHotkeyAdapterFactory` |

입력 어댑터는 `Input/` 에 있다. `Capture/Input/` 에는 미리보기 좌표 계산만 남는다
(`PreviewInputRouter`, `PreviewInputMapper`, `CaptureTargetBounds`, `InputForwardResult`).
입력은 캡처 전용이 아니라서 나눠 두었다.

스캔코드로 키를 넣는 것은 `IScanCodeInput` 으로 따로 뺐다. PostMessage 경로는 그것을
제대로 할 수 없고(대상 IME 가 그렇게 온 한/영 전환을 받지 않는다), 못 하는 것을 늘 false 만
돌려주는 빈 메서드로 두면 부르는 쪽이 되는 줄 알고 쓰기 때문이다.
필요한 쪽은 `adapter is IScanCodeInput` 으로 물어보고, 아니면 못 한다고 말한다.

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

### 편집기

`AvalonEdit`(`ICSharpCode.AvalonEdit`)을 쓴다. **AvaloniaEdit 이 아니다** - 그쪽은 Avalonia 용
포팅이라 WPF 에 올라가지 않는다.

- `TextEditor.Text` 는 의존 속성이 아니다. `Markup/AvalonEditText` 붙임 속성으로 양방향을 잇는다.
- 순수 WPF 컨트롤이라 **경량 테마가 손대지 않는다.** 배경·글자색을 직접 주지 않으면
  어두운 테마에서 이 편집기만 흰 판으로 남는다. `Helper/SequenceScriptHighlighting` 이 색을 든다.
- 테마가 어두운지는 이름(`Dark`·`Black`)으로 가르고, `*System` 테마는 레지스트리의
  `AppsUseLightTheme` 을 본다. DevExpress `Theme` 에는 그걸 알려 주는 것이 없다.
- **`TextEditor` 를 바로 얹으면 창 전체의 UI 자동화 트리가 빈다.** 실측으로 갈렸다 -
  같은 화면의 버튼이 그리드 시절 37개에서 편집기를 넣은 뒤 0개가 됐다. 편집기만이 아니라
  창에 있는 모든 것이 안 보인다(스크린 리더에도 그렇다). AvalonEdit 이 만드는 자동화 피어가
  원인이라, `Markup/ScriptEditor` 로 감싸 평범한 `FrameworkElementAutomationPeer` 를 준다.
  **AvalonEdit 을 다른 화면에 또 쓸 일이 생기면 `ScriptEditor` 를 쓴다.**
- 그래도 편집기 안의 **글**은 UIAutomation 으로 읽고 쓸 수 없다 - 밖에서 검증하려면
  좌표로 클릭해 포커스를 준 뒤 키를 보내야 한다.

## 검증

`Minguk.Tools.Tests` 는 단위 테스트가 아니라 **실행형 하네스**다. 실제 커서와 살아 있는 창을
확인하므로 목으로는 대신할 수 없다. 자세한 것은 `Minguk.Tools.Tests/README.md`.

```
dotnet run --project Minguk.Tools.Tests -c Debug -- --backend=SendInput   # 경로별 전체
dotnet run --project Minguk.Tools.Tests -c Debug -- --views               # 화면 생성만 (안전)
dotnet run --project Minguk.Tools.Tests -c Debug -- --fallback            # 드라이버 없는 상황
dotnet run --project Minguk.Tools.Tests -c Debug -- --calibrate           # 정규화 규칙 실측
```

- `--views` 외에는 **커서와 키보드를 가져간다.** 돌리는 동안 손을 떼야 한다.
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
- `interception.dll` 은 `Minguk.Tools/Native/x64/` 에 있다. 이 프로젝트만 쓰므로 `Libs/` 가 아니다.
  `DllImport` 가 실행 파일 옆에서 찾으므로 `TargetPath` 로 출력 루트에 떨어뜨린다.
  x86 빌드를 잘못 넣으면 컴파일은 통과하고 실행할 때 `BadImageFormatException` 으로 터진다.
- `Minguk.Image` 는 솔루션에 없다. `Libs/Minguk.Image.dll` 을 `Reference` + `HintPath` 로 무는, DirectML 같은 외부 바이너리 취급이다. csproj 에 `<Resource Include>` 가 4만 개라 소스로 두면 빌드마다 그 csproj 평가에 시간을 버리기 때문이다. 아이콘을 바꿔야 하면 `Minguk.Image/Minguk.Image.csproj` 를 단독으로 Release|x64 빌드해서 나온 DLL 로 `Libs/Minguk.Image.dll` 을 덮어쓰고 커밋한다.
