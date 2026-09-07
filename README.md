# Minguk.Tools

TamsTools 의 셸 구조와 기준 스타일만 떼어 낸 새 WPF 프로젝트.
도메인 코드(TAMS · Redis · XPO · MQTT · MCP · AvalonEdit)는 들어 있지 않다.

## 구성

```
Minguk.Tools.sln
├─ Minguk.Base/            기준 스타일과 공통 코드 (TamsTools 에서 복사, 독립 사본)
│   └─ Resource/           ControlTemplate · DataTemplate · GridColumnStyle · Style.xaml
├─ Minguk.Image/           Axialis 아이콘 팩 (TamsTools 에서 복사)
└─ Minguk.Tools/
    ├─ Main.cs                     진입점. Velopack → UserDataPaths → App.Run
    ├─ App.xaml / App.xaml.cs      테마·스플래시·DI·설정·로깅·전역 예외
    ├─ Views/
    │   ├─ MainWindow.xaml         ThemedWindow. 캡션 버튼과 테마 선택
    │   ├─ MainView.xaml           Accordion 네비 + GridSplitter + 문서 탭 + 상태바
    │   ├─ DashboardView.xaml      샘플 1 (LayoutControl)
    │   └─ CaptureMonitorView.xaml 캡처 모니터 (WGC 캡처 + 성능 통계)
    ├─ ViewModels/
    │   ├─ MainWindowViewModel     창 상태(최상위·전체화면·재시작·종료)
    │   ├─ MainViewModel           메뉴 ↔ 문서 탭, 레이아웃 저장·복원
    │   └─ DocumentViewModelBase   문서 탭 제목·아이콘 계약
    ├─ Capture/                    Windows.Graphics.Capture 캡처 파이프라인
    ├─ Source/MainMenu.cs          메뉴 정의 (화면 추가 지점)
    ├─ Locator/MainViewLocator.cs  뷰 이름 → DI 에서 View 인스턴스
    └─ SplashScreen/               시작 스플래시
```

## 빌드

Visual Studio 2022 이상에서 `Minguk.Tools.sln` 을 열고 **x64** 로 빌드한다.
(AnyCPU 는 정의하지 않았다. csproj 가 AnyCPU 를 받으면 x64 로 바꿔 준다.)

DevExpress 26.1.3 NuGet 피드가 설정되어 있어야 한다. TamsTools 와 같은 환경이면 그대로 복원된다.

## 화면 하나 추가하기

1. `Views/XxxView.xaml` (+ `.xaml.cs`) — UserControl. DataContext 는 `{dxmvvm:ViewModelSource Type={x:Type viewModels:XxxViewModel}}`
2. `ViewModels/XxxViewModel.cs` — `DocumentViewModelBase` 상속, `static Create()` 팩터리, 생성자에서 `Caption` / `CaptionImage` 지정
3. `App.xaml.cs` → `builder.Services.AddTransient<XxxView>()`
4. `Source/MainMenu.cs` → `MenuItemModel.Create(...)` 추가. `class_nm` 은 View 의 **전체 타입 이름**

3번을 빠뜨리면 메뉴에는 보이는데 탭이 빈 채로 열린다. `MainViewLocator` 가 DI 에서 뷰를 꺼내기 때문이다.

## 알아 둘 것

- **폰트·크기**는 `BaseFontFamily` / `BaseFontSize` 리소스를 `DynamicResource` 로 참조한다.
  `StaticResource` 로 쓰면 설정에서 폰트를 바꿔도 반영되지 않는다.
- **경량 테마**(`CompatibilitySettings.UseLightweightThemes = true`)를 쓰므로 표준 WPF 컨트롤에는
  테마가 적용되지 않는다. 화면은 DevExpress 컨트롤로 짤 것.
- **아이콘**은 Axialis 팩(`FreeImage`)을 권한다. `DXImageHelper` 는 없는 경로를 주면 런타임에 터지는데,
  Axialis 는 `Minguk.Image` 폴더에서 파일 존재를 눈으로 확인할 수 있다.
- **사용자 설정**은 `%AppData%\Minguk.Tools` 에 저장된다. 실행 폴더에 두면 Velopack 업데이트 때 사라진다.
- **자동 업데이트**는 `Main.cs` 의 `UpdateRepoUrl` 을 실제 릴리스 저장소로 바꿔야 동작한다.
  개발 환경(비설치 실행)에서는 아무 일도 하지 않는다.

## 화면 캡처

캡처는 **Windows.Graphics.Capture(WGC)** 로 한다. 윈도우에서 가장 빠른 경로다.

- DWM 이 합성한 프레임을 GPU 텍스처 그대로 받는다. GDI(`BitBlt`/`PrintWindow`)처럼 CPU 왕복이 없다.
- DXGI Desktop Duplication 과 달리 **창 단위** 캡처가 되고, 창이 가려져 있어도 잡힌다.
- 화면이 갱신될 때만 프레임이 온다. 멈춘 화면에서는 CPU 를 쓰지 않는다.

2560×1440 에서 실측한 프레임당 지연:

| 경로 | 지연 |
|---|---|
| GPU 텍스처만 (`cpuReadback: false`) | 0.31 ms |
| CPU 리드백까지 (BGRA 8888) | 1.66 ms (스테이징 복사 1.41 ms) |

코드는 `Capture/` 에 있다.

| 파일 | 하는 일 |
|---|---|
| `WgcCaptureSession.cs` | 세션 본체. 프레임 풀·디바이스·리드백·전체화면 폴백 |
| `CaptureInterop.cs` | WinRT ↔ D3D11 COM 인터롭 (`IGraphicsCaptureItemInterop`, `IDirect3DDxgiInterfaceAccess`) |
| `CaptureTarget.cs` | 캡처 대상(창/모니터) 열거 |
| `CapturedFrameEventArgs.cs` | 프레임 한 장. 콜백이 도는 동안에만 유효하다 |
| `FrameSnapshot.cs` | 프레임을 PNG 로 저장 (캡처 내용 눈으로 확인용) |

### DirectX 전체화면

- 요즘 게임은 전체화면이라도 flip model 로 DWM 합성 경로에 남아 있어 그대로 잡힌다.
- 진짜 독점 전체화면(legacy FSE, 주로 구형 DX9 이나 "전체 화면 최적화 사용 안 함"을 켠 경우)으로 넘어가면
  **창 캡처**가 멎을 수 있다. 이때는 **모니터 캡처**로 잡으면 된다.
- `WgcCaptureSession` 이 이걸 자동으로 한다. 창을 걸고 1.5초 안에 한 장도 못 받으면
  그 창이 올라가 있는 모니터 캡처로 스스로 갈아탄다 (`AutoFallbackToMonitor`).
- 그래도 안 잡히는 경우는 게임에 API 후킹으로 주입하는 방법밖에 없다(OBS 게임 캡처 방식). 여기엔 없다.

### 주의

- `Capture/` 를 쓰려면 앱 프로젝트 TFM 이 `net10.0-windows10.0.22621.0` 이어야 한다. WinRT 프로젝션이 이 TFM 에서만 들어온다.
  최소 실행 OS 는 `SupportedOSPlatformVersion` 대로 Windows 10 2004(19041) 다.
- 프레임 콜백은 **스레드풀**에서 돈다. 여기서 UI 를 직접 만지면 안 되고, 디스패처로 넘기더라도
  프레임마다 넘기면 캡처 속도가 UI 응답 속도로 떨어진다. `CaptureMonitorViewModel` 은 1초마다 한 번만 넘긴다.
- `CapturedFrameEventArgs.Texture` / `PixelData` 는 핸들러가 끝나면 무효다. 밖으로 들고 나가려면 직접 복사할 것.

## 추론 (ONNX Runtime + DirectML)

캡처한 GPU 텍스처를 모델 입력 텐서로 만들고, DirectML 실행 공급자로 추론한다.
DirectML 이라 GPU 벤더를 안 가린다(NVIDIA · AMD · Intel · 내장 GPU).

| 파일 | 하는 일 |
|---|---|
| `Inference/TensorSpec.cs` | 모델 입력 명세 (크기 · NCHW/NHWC · 레터박스 · 정규화) |
| `Inference/FramePreprocessor.cs` | 컴퓨트 셰이더로 리사이즈 · 정규화 · 축 변환을 한 번에 |
| `Inference/OnnxDmlEngine.cs` | ORT 세션 (DirectML EP) |

### zero-copy 가 아닌 이유

ONNX Runtime 의 C# 바인딩은 `AppendExecutionProvider_DML(deviceId)` **하나만** 노출한다.
D3D12 리소스를 그대로 텐서로 물리는 `OrtDmlApi.CreateGPUAllocationFromD3DResource` 는
C++ 전용이라, 진짜 zero-copy 를 하려면 네이티브 shim DLL 이 필요하다.

대신 GPU 에서 미리 모델 크기로 줄여서 옮길 양을 줄인다. 2560×1440 기준 실측:

| 경로 | 시간 | 옮기는 양 |
|---|---|---|
| 전체 프레임 리드백 (예전) | 1.34 ms | 14.7 MB |
| GPU 전처리 (디스패치) | **0.03 ms** | — |
| 텐서 리드백, 동기 | 1.25 ms | 4.8 MB |
| 텐서 리드백, 파이프라인 | **0.60 ms** | 4.8 MB |

**크기를 1/3 로 줄여도 동기 모드에서는 거의 안 빨라진다.** 리드백 비용을 지배하는 건
전송량이 아니라 GPU 를 기다리는 동기화(약 1 ms 고정)이기 때문이다.
그래서 스테이징 버퍼 두 장을 번갈아 써서, 이번 프레임은 복사만 걸고 지난 프레임을 읽는다.

- `pipelined: true` (기본) — 대기 없음. 대신 텐서가 한 프레임 늦다. 처리량 우선.
- `pipelined: false` — 항상 최신 프레임. 대신 매 프레임 GPU 를 기다린다. 지연 우선.

### 쓰는 법

```csharp
var spec = TensorSpec.Yolo(640);                       // 또는 engine.TryDeriveInputSpec()
using var engine = new OnnxDmlEngine(@"model.onnx");
using var pre = new FramePreprocessor(session.Device!, session.Context!, spec);

session.FrameArrived += (_, e) =>
{
    if (!pre.Process(e.Texture, e.Width, e.Height))
        return;                                        // 파이프라인 첫 프레임

    using var outputs = engine.Run(pre.Tensor, spec.Shape);
    // outputs[0].GetTensorDataAsSpan<float>() 를 모델에 맞게 해석한다
};
```

정규화 값(`Mean` / `Std`)은 ONNX 파일에 안 적혀 있다. 모델 문서를 보고 `TensorSpec` 에 넣어야 한다.
기본값은 0~1 정규화로, YOLO 계열의 관습을 따른다.

## 스타일을 TamsTools 와 계속 맞추려면

`Minguk.Base` / `Minguk.Image` 는 복사본이라 자동으로 동기화되지 않는다.
TamsTools 쪽 `Resource/*.xaml` 이 바뀌면 수동으로 가져와야 한다.
