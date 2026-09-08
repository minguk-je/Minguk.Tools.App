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
| `IInputAdapter` | `SendInputAdapter` | `InputAdapterFactory` |

지키는 것:

- 구현이 하나뿐이어도 인터페이스를 만든다. 둘째가 필요해질 때 부르는 쪽을 안 고치려는 것이다.
- 새 구현을 넣을 때 **팩터리만** 고친다. 부르는 쪽이 구체 타입을 알면 어댑터로 나눈 뜻이 없다.
- 인터페이스에 `Name` 을 둔다. 지금 어느 경로로 도는지 로그·화면에 보여 줄 수 있어야 한다.
- 돌아가는 중에 갈아끼울 수 있으면 그렇게 만든다 (`PreviewInputRouter.InputAdapter` 처럼).

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
- ViewModel 간 통신은 직접 참조 대신 `MessengerUtility` 를 쓴다.
- 예외는 `ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName())` 로 보여 주고 NLog 로 남긴다.
- `Minguk.Image` 는 솔루션에 없다. `Libs/Minguk.Image.dll` 을 `Reference` + `HintPath` 로 무는, DirectML 같은 외부 바이너리 취급이다. csproj 에 `<Resource Include>` 가 4만 개라 소스로 두면 빌드마다 그 csproj 평가에 시간을 버리기 때문이다. 아이콘을 바꿔야 하면 `Minguk.Image/Minguk.Image.csproj` 를 단독으로 Release|x64 빌드해서 나온 DLL 로 `Libs/Minguk.Image.dll` 을 덮어쓰고 커밋한다.
