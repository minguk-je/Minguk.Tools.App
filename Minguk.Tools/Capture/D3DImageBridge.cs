using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

using Vortice.Direct3D11;
using Vortice.Direct3D9;

namespace Minguk.Tools.Capture;

/// <summary>
/// 캡처한 D3D11 텍스처를 CPU 를 거치지 않고 화면에 올린다.
///
/// WPF 의 <see cref="D3DImage"/> 는 D3D9 표면만 받는데 캡처는 D3D11 이다.
/// 그래서 D3D9Ex 로 공유 렌더 타깃을 하나 만들고, 그 공유 핸들을 D3D11 에서 열어
/// 같은 메모리를 양쪽에서 보게 한다.
///
///   [WGC 프레임 텍스처] --CopyResource(GPU)--> [공유 텍스처] == [D3D9 표면] --> D3DImage
///
/// CPU 리드백이 필요 없다. WriteableBitmap 경로는 프레임마다 GPU→CPU→UI 로
/// 2560x1440 기준 30MB 를 옮겨야 해서 60fps 캡처에서 40fps 언저리에 머물렀다.
///
/// 스레드 규칙
///   <see cref="EnsureSurface"/> / <see cref="Present"/> : UI 스레드
///   <see cref="CopyFrom"/>                              : 캡처 콜백 스레드
/// D3D11 디바이스에는 이미 멀티스레드 보호가 켜져 있다(WgcCaptureSession 참조).
///
/// 원격 데스크톱처럼 하드웨어 가속이 없는 환경에서는 만들기가 실패한다.
/// 그때는 호출자가 WriteableBitmap 경로로 돌아가면 된다.
/// </summary>
public sealed class D3DImageBridge : IDisposable
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    private IDirect3D9Ex? _direct3D9;
    private IDirect3DDevice9Ex? _direct3D9Device;
    private IDirect3DSurface9? _sharedSurface;
    private ID3D11Texture2D? _sharedTexture;

    private int _surfaceWidth;
    private int _surfaceHeight;

    /// <summary>마지막 GPU 복사에 걸린 시간(ms). 어디가 느린지 가르는 용도.</summary>
    public double LastCopyMs { get; private set; }

    /// <summary>마지막 화면 반영에 걸린 시간(ms).</summary>
    public double LastPresentMs { get; private set; }

    /// <summary>
    /// WPF 가 이 표면을 실제로 쓸 수 있는 상태인지.
    /// false 면 TryLock 이 즉시 실패한다 — 표면이 WPF 가 그리는 어댑터에 없을 때 그렇다.
    /// </summary>
    public bool IsFrontBufferAvailable => Image.IsFrontBufferAvailable;

    /// <summary>XAML 의 Image 가 무는 소스. D3DImage 도 ImageSource 다.</summary>
    public D3DImage Image { get; } = new();

    /// <summary>공유 표면이 준비됐는지. 준비 전에는 <see cref="CopyFrom"/> 이 아무것도 하지 않는다.</summary>
    public bool IsReady => _sharedSurface is not null && _sharedTexture is not null;

    /// <summary>
    /// 주어진 크기의 공유 표면을 준비한다. 크기가 그대로면 아무것도 하지 않는다.
    /// UI 스레드에서 부를 것 — D3DImage 를 만진다.
    /// </summary>
    public bool EnsureSurface(ID3D11Device device11, int width, int height)
    {
        if (IsReady && _surfaceWidth == width && _surfaceHeight == height)
            return true;

        ReleaseSurface();

        _direct3D9 ??= D3D9.Direct3DCreate9Ex();

        // 화면에 아무것도 내보내지 않는 더미 디바이스다. 백버퍼는 1x1 로 충분하다.
        // Multithreaded 는 필수 — 캡처 스레드와 UI 스레드가 같은 표면을 만진다.
        _direct3D9Device ??= _direct3D9.CreateDeviceEx(
            adapter: 0,
            deviceType: DeviceType.Hardware,
            focusWindow: GetDesktopWindow(),
            createFlags: CreateFlags.HardwareVertexProcessing | CreateFlags.Multithreaded | CreateFlags.FpuPreserve,
            presentationParameters: new PresentParameters
            {
                Windowed = true,
                SwapEffect = SwapEffect.Discard,
                DeviceWindowHandle = GetDesktopWindow(),
                PresentationInterval = PresentInterval.Immediate,
                BackBufferFormat = Format.Unknown,
                BackBufferWidth = 1,
                BackBufferHeight = 1
            });

        // 공유 핸들을 달아 렌더 타깃을 만든다. A8R8G8B8 은 DXGI 의 B8G8R8A8_UNORM 과 같은 배치라
        // WGC 프레임(BGRA)을 CopyResource 로 그대로 옮길 수 있다.
        IntPtr sharedHandle = IntPtr.Zero;
        _sharedSurface = _direct3D9Device.CreateRenderTarget(
            (uint)width,
            (uint)height,
            Format.A8R8G8B8,
            MultisampleType.None,
            0,
            lockable: false,
            ref sharedHandle);

        if (sharedHandle == IntPtr.Zero)
        {
            ReleaseSurface();
            return false;
        }

        // 같은 메모리를 D3D11 쪽에서 연다.
        _sharedTexture = device11.OpenSharedResource<ID3D11Texture2D>(sharedHandle);

        _surfaceWidth = width;
        _surfaceHeight = height;

        Image.Lock();
        try
        {
            Image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, _sharedSurface.NativePointer);
        }
        finally
        {
            Image.Unlock();
        }

        return true;
    }

    /// <summary>
    /// 캡처 프레임을 공유 표면으로 복사한다. GPU 안에서 끝나고 CPU 는 건드리지 않는다.
    /// 캡처 콜백 스레드에서 부를 것 — 프레임 텍스처가 그 안에서만 유효하다.
    /// </summary>
    public bool CopyFrom(ID3D11DeviceContext context, ID3D11Texture2D source)
    {
        if (!IsReady)
            return false;

        var start = System.Diagnostics.Stopwatch.GetTimestamp();

        context.CopyResource(_sharedTexture!, source);

        // 복사를 GPU 에 밀어 넣는다. 이게 없으면 UI 가 이전 프레임을 보게 된다.
        context.Flush();

        LastCopyMs = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        return true;
    }

    /// <summary>
    /// 바뀐 영역을 WPF 에 알린다. UI 스레드에서 부를 것.
    ///
    /// TryLock 은 쓰지 않는다. 타임아웃을 0 으로 두든 4ms 로 두든 매번 즉시 실패했다
    /// (프론트버퍼가 살아 있는데도 0fps). Lock() 은 실측으로 57~60fps 를 안정적으로 냈다.
    /// </summary>
    /// <returns>화면에 반영했으면 true.</returns>
    public bool Present()
    {
        if (!IsReady)
            return false;

        var start = System.Diagnostics.Stopwatch.GetTimestamp();

        Image.Lock();
        try
        {
            Image.AddDirtyRect(new Int32Rect(0, 0, _surfaceWidth, _surfaceHeight));
        }
        finally
        {
            Image.Unlock();
        }

        LastPresentMs = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        return true;
    }

    private void ReleaseSurface()
    {
        _sharedTexture?.Dispose();
        _sharedTexture = null;

        _sharedSurface?.Dispose();
        _sharedSurface = null;

        _surfaceWidth = 0;
        _surfaceHeight = 0;
    }

    public void Dispose()
    {
        ReleaseSurface();

        _direct3D9Device?.Dispose();
        _direct3D9Device = null;

        _direct3D9?.Dispose();
        _direct3D9 = null;
    }
}
